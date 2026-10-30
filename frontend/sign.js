// Signs requests to the API on the server side, because a secret in the React bundle would be public.
// Must produce the same signature as HmacSignatureVerifier.Sign in InventoryHold.Domain.
import crypto from 'crypto'

function timestamp() {
  return String(Math.floor(Date.now() / 1000))
}

function signature(r) {
  const bodyHash = crypto.createHash('sha256').update(r.requestBuffer || '').digest('hex')
  const stringToSign = [r.method, r.variables.request_uri, r.variables.hmac_timestamp, r.variables.request_id, bodyHash].join('\n')
  return crypto.createHmac('sha256', process.env.HMAC_WEB_SECRET || '').update(stringToSign).digest('hex')
}

export default { timestamp, signature }
