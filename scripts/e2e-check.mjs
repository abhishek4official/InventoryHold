#!/usr/bin/env node
// End-to-end check against the running stack. No dependencies; needs Node 18+.
//
//   docker-compose up --build -d
//   node scripts/e2e-check.mjs
//
// To include the expiry check, start the stack with short holds first:
//   HOLD_DURATION_MINUTES=0.25 HOLD_SWEEP_INTERVAL_SECONDS=5 docker-compose up --build -d
//
// Flags: --api-only skips the RabbitMQ and Redis checks.

import { execFileSync } from 'node:child_process'

const API = process.env.API_URL ?? 'http://localhost:8080'
const RABBIT = process.env.RABBIT_URL ?? 'http://localhost:15672'
const RABBIT_AUTH = `${process.env.RABBITMQ_USER ?? 'inventory'}:${process.env.RABBITMQ_PASSWORD ?? 'inventory-dev-only'}`
const QUEUE = 'inventory-hold.events'
const API_ONLY = process.argv.includes('--api-only')

let failures = 0
function check(name, passed, detail = '') {
  console.log(`${passed ? 'PASS' : 'FAIL'}  ${name}${!passed && detail ? ` (${detail})` : ''}`)
  if (!passed) failures++
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

async function call(method, path, body) {
  const response = await fetch(API + path, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  })
  const text = await response.text()
  return { status: response.status, headers: response.headers, body: text ? JSON.parse(text) : null }
}

const placeHold = (sku, quantity, customerId = 'e2e') =>
  call('POST', '/api/holds', { customerId, items: [{ sku, quantity }] })

async function stockOf(sku) {
  const { body } = await call('GET', '/api/inventory')
  return body.find((item) => item.sku === sku)
}

/**
 * Reads events off the queue without consuming them. Asks for a fixed large count, because the
 * management API's queue depth is a sampled statistic that lags behind recent publishes.
 */
async function rabbitEvents(count = 5000) {
  const response = await fetch(`${RABBIT}/api/queues/%2F/${QUEUE}/get`, {
    method: 'POST',
    headers: {
      Authorization: `Basic ${Buffer.from(RABBIT_AUTH).toString('base64')}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ count, ackmode: 'ack_requeue_true', encoding: 'auto' }),
  })
  if (!response.ok) return []
  return (await response.json()).map((message) => JSON.parse(message.payload))
}

function redisCli(...args) {
  return execFileSync('docker', ['compose', 'exec', '-T', 'redis', 'redis-cli', ...args], { encoding: 'utf8' }).trim()
}

async function endpoints() {
  console.log('\n-- Endpoints and status codes')

  const inventory = await call('GET', '/api/inventory')
  check('GET /api/inventory is 200', inventory.status === 200)
  check('at least 5 products are seeded', inventory.body.length >= 5, `got ${inventory.body?.length}`)

  const sku = inventory.body.find((item) => item.available >= 5).sku
  const before = await stockOf(sku)

  const created = await placeHold(sku, 2)
  check('POST /api/holds is 201', created.status === 201, `got ${created.status}`)
  check('POST returns a Location header', !!created.headers.get('location'))
  check('new hold is Active', created.body.status === 'Active')
  check('hold has an expiry', new Date(created.body.expiresAt) > new Date(created.body.createdAt))

  const during = await stockOf(sku)
  check('stock moves from available to held', during.available === before.available - 2 && during.held === before.held + 2)

  const fetched = await call('GET', `/api/holds/${created.body.id}`)
  check('GET /api/holds/{id} is 200', fetched.status === 200)
  const fetchedAgain = await call('GET', `/api/holds/${created.body.id}`)
  check('a repeat GET returns the same hold', fetchedAgain.body.id === created.body.id)

  const listed = await call('GET', '/api/holds?status=Active')
  check('GET /api/holds?status=Active lists it', listed.body.some((hold) => hold.id === created.body.id))

  check('invalid status filter is 400', (await call('GET', '/api/holds?status=Nonsense')).status === 400)
  check('unknown hold GET is 404', (await call('GET', '/api/holds/does-not-exist')).status === 404)
  check('unknown hold DELETE is 404', (await call('DELETE', '/api/holds/does-not-exist')).status === 404)
  check('empty items is 400', (await call('POST', '/api/holds', { customerId: 'e2e', items: [] })).status === 400)
  check('zero quantity is 400', (await placeHold(sku, 0)).status === 400)
  check('missing body fields are 400', (await call('POST', '/api/holds', {})).status === 400)
  check('unknown SKU is 404', (await placeHold('NO-SUCH-SKU', 1)).status === 404)

  const tooMany = await placeHold(sku, 1000)
  check('over-request is 409', tooMany.status === 409, `got ${tooMany.status}`)
  check('errors are ProblemDetails with a code', tooMany.body?.code === 'insufficient_stock')

  const partial = await call('POST', '/api/holds', {
    customerId: 'e2e',
    items: [{ sku, quantity: 1 }, { sku: inventory.body.find((item) => item.sku !== sku).sku, quantity: 1000 }],
  })
  const afterPartial = await stockOf(sku)
  check('multi-item hold with one short line is 409', partial.status === 409)
  check('a failed multi-item hold leaves stock untouched', afterPartial.available === during.available, `${afterPartial.available} vs ${during.available}`)

  const released = await call('DELETE', `/api/holds/${created.body.id}`)
  check('DELETE is 200 and Released', released.status === 200 && released.body.status === 'Released')
  const after = await stockOf(sku)
  check('release restores stock', after.available === before.available && after.held === before.held)

  const readBack = await call('GET', `/api/holds/${created.body.id}`)
  check('GET after release shows Released, not a stale cache entry', readBack.body.status === 'Released', readBack.body.status)
  const releasedTwice = await call('DELETE', `/api/holds/${created.body.id}`)
  check('releasing twice is 409', releasedTwice.status === 409, `got ${releasedTwice.status}`)
}

async function races() {
  console.log('\n-- Races against real MongoDB')

  const { body: inventory } = await call('GET', '/api/inventory')
  const target = inventory.reduce((lowest, item) =>
    item.available > 0 && item.available < lowest.available ? item : lowest)
  const attempts = target.available + 40

  const results = await Promise.all(Array.from({ length: attempts }, () => placeHold(target.sku, 1)))
  const won = results.filter((result) => result.status === 201)
  const lost = results.filter((result) => result.status === 409)
  check(`${attempts} parallel holds on ${target.available} units: exactly ${target.available} win`,
    won.length === target.available, `won ${won.length}`)
  check('every other attempt gets 409', lost.length === attempts - target.available, `lost ${lost.length}`)

  const soldOut = await stockOf(target.sku)
  check('available reaches exactly 0, never negative', soldOut.available === 0, `available ${soldOut.available}`)

  await Promise.all(won.map((result) => call('DELETE', `/api/holds/${result.body.id}`)))
  const restored = await stockOf(target.sku)
  check('releasing them all restores the original stock', restored.available === target.available, `available ${restored.available}`)

  const hold = (await placeHold(target.sku, 1)).body
  const releases = await Promise.all(Array.from({ length: 20 }, () => call('DELETE', `/api/holds/${hold.id}`)))
  check('20 parallel releases: exactly one 200', releases.filter((r) => r.status === 200).length === 1)
  check('the other 19 get 409', releases.filter((r) => r.status === 409).length === 19)
  const once = await stockOf(target.sku)
  check('stock is restored exactly once', once.available === target.available, `available ${once.available}`)
}

async function expiry() {
  console.log('\n-- Expiry')

  const { body: inventory } = await call('GET', '/api/inventory')
  const sku = inventory.find((item) => item.available >= 3).sku
  const before = await stockOf(sku)

  const swept = (await placeHold(sku, 1)).body
  const lazily = (await placeHold(sku, 1)).body
  const durationMs = new Date(swept.expiresAt) - new Date(swept.createdAt)
  if (durationMs > 60_000) {
    console.log(`SKIP  holds last ${durationMs / 1000}s; start with HOLD_DURATION_MINUTES=0.25 to test expiry`)
    await call('DELETE', `/api/holds/${swept.id}`)
    await call('DELETE', `/api/holds/${lazily.id}`)
    return false
  }

  await sleep(durationMs + 500)
  const delayed = await call('DELETE', `/api/holds/${lazily.id}`)
  check('DELETE of an expired hold is 410', delayed.status === 410, `got ${delayed.status}`)
  const shown = await call('GET', `/api/holds/${lazily.id}`)
  check('GET of an expired hold is 200 with status Expired', shown.status === 200 && shown.body.status === 'Expired')

  // Nothing touches this hold, so only the sweeper can restore its stock.
  let after = await stockOf(sku)
  for (let i = 0; i < 40 && after.available !== before.available; i++) {
    await sleep(1000)
    after = await stockOf(sku)
  }
  check('the sweeper restores stock with no request touching the hold', after.available === before.available,
    `${after.available} vs ${before.available}`)
  check('held count returns to its starting value', after.held === before.held)
  return true
}

async function messaging(expiryRan) {
  console.log('\n-- RabbitMQ')

  const events = await rabbitEvents()
  check('the events queue exists and has messages', events.length > 0)

  const types = new Set(events.map((event) => event.eventType))
  check('HoldCreated was published', types.has('HoldCreated'))
  check('HoldReleased was published', types.has('HoldReleased'))
  if (expiryRan) check('HoldExpired was published', types.has('HoldExpired'))

  const sample = events.find((event) => event.eventType === 'HoldCreated')
  const complete = sample && sample.eventId && sample.holdId && sample.customerId && sample.items?.[0]?.sku && sample.expiresAt
  check('event payload has the context a consumer needs', !!complete, JSON.stringify(sample))
}

async function caching() {
  console.log('\n-- Redis')

  const { body: inventory } = await call('GET', '/api/inventory')
  const key = 'inventory-hold:inventory'
  check('GET /api/inventory populates the cache', redisCli('EXISTS', key) === '1')
  const ttl = Number(redisCli('TTL', key))
  check('the inventory key has a TTL of 30s or less', ttl > 0 && ttl <= 30, `ttl ${ttl}`)

  const sku = inventory.find((item) => item.available >= 1).sku
  const hold = (await placeHold(sku, 1)).body
  check('a hold invalidates the inventory key', redisCli('EXISTS', key) === '0')
  const fresh = await stockOf(sku)
  check('the next read shows the new stock', fresh.available === inventory.find((i) => i.sku === sku).available - 1)

  await call('GET', `/api/holds/${hold.id}`)
  const holdKey = `inventory-hold:hold:${hold.id}`
  check('GET /api/holds/{id} populates the cache', redisCli('EXISTS', holdKey) === '1')
  await call('DELETE', `/api/holds/${hold.id}`)
  check('release invalidates the hold key', redisCli('EXISTS', holdKey) === '0')
}

await endpoints()
await races()
const expiryRan = await expiry()
if (!API_ONLY) {
  await caching()
  await messaging(expiryRan)
}

console.log(failures === 0 ? '\nAll checks passed.' : `\n${failures} check(s) FAILED.`)
process.exit(failures === 0 ? 0 : 1)
