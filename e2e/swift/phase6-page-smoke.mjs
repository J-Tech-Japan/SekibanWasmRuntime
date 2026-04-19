import { chromium } from 'playwright';
const browser = await chromium.launch();
const ctx = await browser.newContext();

const blazorPages = [
  '/', '/classrooms', '/students', '/enrollments', '/weather',
  '/mv', '/memory', '/login'
];
const webnextPages = [
  '/', '/classrooms', '/students', '/enrollments', '/weather',
  '/meeting-rooms', '/reservations', '/users', '/approvals', '/login', '/register'
];

async function probe(origin, path) {
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(`[pageerror] ${e.message}`));
  page.on('requestfailed', r => {
    const u = r.url();
    if (!u.includes('_blazor') && !u.includes('_next/webpack-hmr')) {
      errors.push(`[reqfail] ${u} -- ${r.failure()?.errorText}`);
    }
  });
  try {
    const res = await page.goto(origin + path, { waitUntil: 'networkidle', timeout: 45000 });
    const status = res?.status() ?? 0;
    const title = await page.title().catch(() => '');
    const h1 = (await page.locator('h1,h2').first().textContent().catch(() => '') ?? '').trim().slice(0, 60);
    console.log(`${status === 200 && errors.length === 0 ? 'OK ' : 'BAD'} ${origin}${path} → ${status} title=${title.slice(0,40)} h1=${h1}`);
    errors.forEach(e => console.log('  ' + e));
  } catch (err) {
    console.log(`ERR ${origin}${path}: ${err.message}`);
  }
  await page.close();
}

console.log('=== Blazor (http://127.0.0.1:6380) ===');
for (const p of blazorPages) await probe('http://127.0.0.1:6380', p);

console.log('\n=== WebNext Next.js (http://127.0.0.1:6381) ===');
for (const p of webnextPages) await probe('http://127.0.0.1:6381', p);

await browser.close();
