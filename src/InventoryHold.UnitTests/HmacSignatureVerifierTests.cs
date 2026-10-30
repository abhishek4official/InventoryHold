using System.Globalization;
using System.Text;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace InventoryHold.UnitTests;

[TestFixture]
public class HmacSignatureVerifierTests
{
    private const string Secret = "unit-test-secret-at-least-32-characters-long";
    private const string Client = "checkout";
    private const string Nonce = "nonce-1";
    private const string Target = "/api/holds?status=Active";

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"customerId\":\"c1\"}");

    private Mock<INonceStore> _nonces = null!;
    private FakeTimeProvider _clock = null!;
    private HmacSignatureVerifier _verifier = null!;

    [SetUp]
    public void SetUp()
    {
        _nonces = new Mock<INonceStore>();
        _nonces.Setup(n => n.TryRememberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        var options = new HmacOptions { Clients = new() { [Client] = Secret, ["disabled"] = "" } };
        _verifier = new HmacSignatureVerifier(Options.Create(options), _nonces.Object, _clock, NullLogger<HmacSignatureVerifier>.Instance);
    }

    private string Now => _clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>A correctly signed POST. Tests change one thing about it to simulate an attack.</summary>
    private HmacRequest Signed(string? timestamp = null, string secret = Secret, string client = Client)
    {
        var at = timestamp ?? Now;
        return new HmacRequest("POST", Target, client, at, Nonce, HmacSignatureVerifier.Sign(secret, "POST", Target, at, Nonce, Body), Body);
    }

    private void VerifyNonceStoreNotUsed() =>
        _nonces.Verify(n => n.TryRememberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);

    [Test]
    public async Task Correctly_signed_request_is_accepted() =>
        Assert.That(await _verifier.VerifyAsync(Signed(), CancellationToken.None), Is.Null);

    [Test]
    public void Signature_matches_the_published_test_vector()
    {
        // Computed separately by Node's crypto module; it proves C# and JavaScript signers agree.
        var signature = HmacSignatureVerifier.Sign(
            "test-vector-secret-0123456789abcdef", "POST", "/api/holds", "1767268800", "abc123", Encoding.UTF8.GetBytes("{\"a\":1}"));

        Assert.That(signature, Is.EqualTo("f4e672321a3790cf6da5db2cf30b176994289552954c1eb5e9dba9e3b18a17f7"));
    }

    [Test]
    public void Empty_body_hashes_consistently() =>
        Assert.That(
            HmacSignatureVerifier.Sign(Secret, "GET", "/api/inventory", "1", "n", ReadOnlySpan<byte>.Empty),
            Is.EqualTo(HmacSignatureVerifier.Sign(Secret, "GET", "/api/inventory", "1", "n", [])));

    [TestCase(null, "t", "n", "s")]
    [TestCase("c", null, "n", "s")]
    [TestCase("c", "t", null, "s")]
    [TestCase("c", "t", "n", null)]
    [TestCase("", "t", "n", "s")]
    public async Task Missing_header_is_rejected(string? client, string? timestamp, string? nonce, string? signature)
    {
        var result = await _verifier.VerifyAsync(new HmacRequest("GET", "/api/inventory", client, timestamp, nonce, signature, Body), CancellationToken.None);

        Assert.That(result, Is.EqualTo(HmacFailure.MissingSignature));
    }

    [Test]
    public async Task Changed_method_is_rejected() =>
        Assert.That(await _verifier.VerifyAsync(Signed() with { Method = "DELETE" }, CancellationToken.None), Is.EqualTo(HmacFailure.InvalidSignature));

    [Test]
    public async Task Changed_path_is_rejected() =>
        Assert.That(await _verifier.VerifyAsync(Signed() with { PathAndQuery = "/api/holds/other?status=Active" }, CancellationToken.None), Is.EqualTo(HmacFailure.InvalidSignature));

    [Test]
    public async Task Changed_query_is_rejected() =>
        Assert.That(await _verifier.VerifyAsync(Signed() with { PathAndQuery = "/api/holds?status=Expired" }, CancellationToken.None), Is.EqualTo(HmacFailure.InvalidSignature));

    [Test]
    public async Task Changed_body_is_rejected() =>
        Assert.That(await _verifier.VerifyAsync(Signed() with { Body = Encoding.UTF8.GetBytes("{\"customerId\":\"c2\"}") }, CancellationToken.None), Is.EqualTo(HmacFailure.InvalidSignature));

    [Test]
    public async Task Changed_timestamp_is_rejected_by_the_signature() =>
        Assert.That(await _verifier.VerifyAsync(Signed() with { Timestamp = "1767268801" }, CancellationToken.None), Is.EqualTo(HmacFailure.InvalidSignature));

    [Test]
    public async Task Changed_nonce_is_rejected_by_the_signature() =>
        Assert.That(await _verifier.VerifyAsync(Signed() with { Nonce = "nonce-2" }, CancellationToken.None), Is.EqualTo(HmacFailure.InvalidSignature));

    [Test]
    public async Task Wrong_secret_is_rejected_without_touching_the_nonce_store()
    {
        var result = await _verifier.VerifyAsync(Signed(secret: "another-secret-also-at-least-32-characters"), CancellationToken.None);

        Assert.That(result, Is.EqualTo(HmacFailure.InvalidSignature));
        VerifyNonceStoreNotUsed();
    }

    [Test]
    public async Task Unknown_client_is_rejected_the_same_way_as_a_bad_signature() =>
        Assert.That(await _verifier.VerifyAsync(Signed(client: "stranger"), CancellationToken.None), Is.EqualTo(HmacFailure.InvalidSignature));

    [Test]
    public async Task Client_with_a_blank_secret_cannot_sign_with_an_empty_key() =>
        Assert.That(await _verifier.VerifyAsync(Signed(secret: "", client: "disabled"), CancellationToken.None), Is.EqualTo(HmacFailure.InvalidSignature));

    [TestCase(-300, null, TestName = "Exactly 300 seconds old is accepted")]
    [TestCase(300, null, TestName = "Exactly 300 seconds ahead is accepted")]
    [TestCase(-301, HmacFailure.StaleTimestamp, TestName = "Over 300 seconds old is rejected")]
    [TestCase(301, HmacFailure.StaleTimestamp, TestName = "Over 300 seconds ahead is rejected")]
    public async Task Timestamp_window(int offsetSeconds, HmacFailure? expected)
    {
        var timestamp = _clock.GetUtcNow().AddSeconds(offsetSeconds).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        Assert.That(await _verifier.VerifyAsync(Signed(timestamp), CancellationToken.None), Is.EqualTo(expected));
    }

    [TestCase("not-a-number")]
    [TestCase("-5")]
    [TestCase("99999999999999999")]
    public async Task Unparseable_timestamp_is_rejected_even_when_correctly_signed(string timestamp) =>
        Assert.That(await _verifier.VerifyAsync(Signed(timestamp), CancellationToken.None), Is.EqualTo(HmacFailure.StaleTimestamp));

    [Test]
    public async Task Stale_request_does_not_touch_the_nonce_store()
    {
        var old = _clock.GetUtcNow().AddHours(-1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        await _verifier.VerifyAsync(Signed(old), CancellationToken.None);

        VerifyNonceStoreNotUsed();
    }

    [Test]
    public async Task Replayed_nonce_is_rejected()
    {
        _nonces.Setup(n => n.TryRememberAsync(Client, Nonce, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.That(await _verifier.VerifyAsync(Signed(), CancellationToken.None), Is.EqualTo(HmacFailure.ReplayedRequest));
    }

    [Test]
    public async Task Nonce_is_remembered_for_longer_than_the_timestamp_window()
    {
        await _verifier.VerifyAsync(Signed(), CancellationToken.None);

        _nonces.Verify(n => n.TryRememberAsync(Client, Nonce, TimeSpan.FromSeconds(600), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Valid_request_is_accepted_when_the_nonce_store_is_down()
    {
        _nonces.Setup(n => n.TryRememberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis unreachable"));

        Assert.That(await _verifier.VerifyAsync(Signed(), CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Forged_request_is_rejected_when_the_nonce_store_is_down()
    {
        _nonces.Setup(n => n.TryRememberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis unreachable"));

        Assert.That(await _verifier.VerifyAsync(Signed(secret: "another-secret-also-at-least-32-characters"), CancellationToken.None), Is.EqualTo(HmacFailure.InvalidSignature));
    }

    [TestCase(true, "", false, TestName = "Enabled with no usable secret is invalid")]
    [TestCase(true, "too-short", false, TestName = "Enabled with a short secret is invalid")]
    [TestCase(true, Secret, true, TestName = "Enabled with a long secret is valid")]
    public void Secrets_are_validated(bool enabled, string secret, bool usable) =>
        Assert.That(new HmacOptions { Enabled = enabled, Clients = new() { [Client] = secret } }.HasUsableClients, Is.EqualTo(usable));

    [Test]
    public void Protection_is_on_unless_turned_off() =>
        Assert.That(new HmacOptions().Enabled, Is.True);
}
