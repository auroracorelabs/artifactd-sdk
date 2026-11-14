const theme = document.querySelector('.theme');
let preference; const system = matchMedia('(prefers-color-scheme: light)');
try { preference = localStorage.getItem('artifactd-theme'); } catch {}
const applyTheme = () => {
  const value = preference === 'light' || preference === 'dark' ? preference : system.matches ? 'light' : 'dark';
  document.documentElement.dataset.theme = value;
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', value === 'light' ? '#f5f9fa' : '#0b1f2a');
  theme?.setAttribute('aria-label', value === 'light' ? 'Switch to dark theme' : 'Switch to light theme');
};
applyTheme();
theme?.addEventListener('click', () => { preference = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark'; try { localStorage.setItem('artifactd-theme', preference); } catch {} applyTheme(); });
system.addEventListener('change', applyTheme);
window.addEventListener('storage', event => { if (event.key === 'artifactd-theme') { preference = event.newValue; applyTheme(); } });
const menu = document.querySelector('.menu-toggle');
menu?.addEventListener('click', () => { const open = menu.getAttribute('aria-expanded') !== 'true'; menu.setAttribute('aria-expanded', String(open)); document.body.toggleAttribute('data-nav-open', open); });
for (const block of document.querySelectorAll('article > pre, article li > pre')) {
  const button = document.createElement('button'); button.type = 'button'; button.className = 'copy'; button.textContent = 'Copy';
  button.setAttribute('aria-label', 'Copy code example');
  button.addEventListener('click', async () => { try { await navigator.clipboard.writeText(block.querySelector('code').textContent); button.textContent = 'Copied'; } catch { button.textContent = 'Select code'; } setTimeout(() => button.textContent = 'Copy', 1800); });
  block.append(button);
}
const search = document.querySelector('#search'); const results = document.querySelector('#search-results'); let index;
search?.addEventListener('input', async () => {
  const query = search.value.trim().toLowerCase(); results.replaceChildren(); results.hidden = !query;
  if (!query) return;
  try {
    index ??= fetch(document.documentElement.dataset.base + 'search.json').then(response => { if (!response.ok) throw new Error('Search unavailable'); return response.json(); });
    const pages = await index;
    if (search.value.trim().toLowerCase() !== query) return;
    const hits = pages.filter(page => page.text.toLowerCase().includes(query)).sort((a,b) => Number(b.title.toLowerCase().includes(query)) - Number(a.title.toLowerCase().includes(query))).slice(0,8);
    if (!hits.length) results.textContent = 'No matching pages. Try another search.';
    for (const page of hits) { const link = document.createElement('a'); link.href = page.url; link.textContent = page.title; results.append(link); }
  } catch { index = undefined; results.textContent = 'Search is unavailable. Use the navigation links.'; }
});
