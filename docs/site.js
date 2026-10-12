const menuButton = document.querySelector('.menu-toggle');
const mobileMenu = document.querySelector('#mobile-menu');
const appMenu = document.querySelector('.nav-dropdown');

function closeNavigation() {
  mobileMenu.hidden = true;
  menuButton.setAttribute('aria-expanded', 'false');
  menuButton.setAttribute('aria-label', 'Open navigation');
  appMenu.open = false;
}

menuButton.addEventListener('click', () => {
  const open = menuButton.getAttribute('aria-expanded') !== 'true';
  mobileMenu.hidden = !open;
  menuButton.setAttribute('aria-expanded', String(open));
  menuButton.setAttribute('aria-label', open ? 'Close navigation' : 'Open navigation');
});

document.addEventListener('click', event => {
  if (event.target.closest('.mobile-menu a, .dropdown a')) closeNavigation();
  if (!event.target.closest('.nav-dropdown')) appMenu.open = false;
});

document.addEventListener('keydown', event => {
  if (event.key === 'Escape') {
    const wasOpen = !mobileMenu.hidden;
    closeNavigation();
    if (wasOpen) menuButton.focus();
  }
});

window.matchMedia('(min-width: 761px)').addEventListener('change', event => {
  if (event.matches) closeNavigation();
});
