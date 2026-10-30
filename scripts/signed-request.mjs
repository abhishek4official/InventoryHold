#!/usr/bin/env node
// Makes one signed API call, for trying the API by hand when HMAC is on. No dependencies; needs Node 18+.
//
//   HMAC_CHECKOUT_SECRET=... node scripts/signed-request.mjs GET api/inventory
//   HMAC_CHECKOUT_SECRET=... node scripts/signed-request.mjs POST api/holds '{"customerId":"c1","items":[{"sku":"MS-WL-02","quantity":1}]}'
//
// The path has no leading slash because Git Bash on Windows would rewrite "/api/..." into a file path.

import { signedHeaders } from './hmac.mjs'

const [method, target, body = ''] = process.argv.slice(2)
const secret = process.env.HMAC_CHECKOUT_SECRET

if (!method || !target || !secret) {
  console.error('Usage: HMAC_CHECKOUT_SECRET=... node scripts/signed-request.mjs METHOD api/PATH [JSON_BODY]')
  process.exit(2)
}

const path = `/${target.replace(/^\/+/, '')}`

const response = await fetch((process.env.API_URL ?? 'http://localhost:8080') + path, {
  method,
  headers: {
    ...(body ? { 'Content-Type': 'application/json' } : {}),
    ...signedHeaders({ secret, method, path, body }),
  },
  body: body || undefined,
})

console.log(`HTTP ${response.status}`)
console.log(await response.text())
process.exit(response.ok ? 0 : 1)
