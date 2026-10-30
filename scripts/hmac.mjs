// Client side of the API's HMAC scheme. Must match HmacSignatureVerifier.Sign in InventoryHold.Domain.
import { createHash, createHmac, randomUUID } from 'node:crypto'

export function signedHeaders({
  secret,
  clientId = 'checkout',
  method,
  path,
  body = '',
  timestamp = String(Math.floor(Date.now() / 1000)),
  nonce = randomUUID(),
}) {
  const bodyHash = createHash('sha256').update(body).digest('hex')
  const stringToSign = [method, path, timestamp, nonce, bodyHash].join('\n')
  return {
    'X-Client-Id': clientId,
    'X-Timestamp': timestamp,
    'X-Nonce': nonce,
    'X-Signature': createHmac('sha256', secret).update(stringToSign).digest('hex'),
  }
}
