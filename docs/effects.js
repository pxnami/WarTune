(() => {
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
  const finePointer = window.matchMedia('(hover: hover) and (pointer: fine)');
  const typing = new Map();

  function stopTyping(element, restore = true) {
    const task = typing.get(element);
    if (!task) return;
    clearTimeout(task.timer);
    if (restore) element.textContent = task.text;
    typing.delete(element);
  }

  function typeText(element, text, delay = 0, speed = 30) {
    stopTyping(element);
    if (reducedMotion.matches) { element.textContent = text; return; }
    const task = { text, index: 0, timer: null };
    typing.set(element, task);
    element.textContent = '';
    const next = () => {
      element.textContent = text.slice(0, ++task.index);
      if (task.index === text.length) { typing.delete(element); return; }
      const character = text[task.index - 1];
      task.timer = setTimeout(next, character === '.' ? speed * 5 : speed);
    };
    task.timer = setTimeout(next, delay);
  }

  function prepareText() {
    const description = document.querySelector('.typed-description');
    typeText(description, description.innerText, 650);

    const wordmark = document.querySelector('.header .brand-word');
    const brand = wordmark.closest('a');
    brand.style.minWidth = `${brand.getBoundingClientRect().width}px`;
    brand.addEventListener('pointerenter', event => {
      if (event.pointerType !== 'touch') typeText(wordmark, '/pxnami', 0, 40);
    });
    brand.addEventListener('pointerleave', () => {
      stopTyping(wordmark, false);
      wordmark.textContent = 'WARTUNE';
    });

    document.querySelectorAll('.terminal-link').forEach(link => {
      const description = link.querySelector('small');
      const text = description.textContent;
      const title = description.parentElement.firstChild.textContent.trim();
      link.setAttribute('aria-label', `${title}. ${text}`);
      description.setAttribute('aria-hidden', 'true');
      link.addEventListener('pointerenter', event => {
        if (event.pointerType !== 'touch') typeText(description, text, 0, 22);
      });
      link.addEventListener('pointerleave', () => stopTyping(description));
    });
  }

  function prepareReveals() {
    if (reducedMotion.matches || !('IntersectionObserver' in window)) return;
    const observer = new IntersectionObserver(entries => {
      entries.forEach(entry => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('revealed');
        observer.unobserve(entry.target);
      });
    }, { threshold: 0.06, rootMargin: '0px 0px -36px 0px' });
    document.querySelectorAll('.section, .download-section').forEach(section => {
      section.classList.add('scroll-reveal');
      observer.observe(section);
    });
  }

  function prepareGrid() {
    const canvas = document.querySelector('.ambient-grid');
    const context = canvas.getContext('2d');
    if (!context) return;
    const spacing = 50;
    const radius = 280;
    let width = 0;
    let height = 0;
    let pointer = null;
    let frame = null;

    function draw() {
      frame = null;
      if (document.hidden) return;
      context.clearRect(0, 0, width, height);
      context.beginPath();
      for (let x = 0; x <= width; x += spacing) { context.moveTo(x, 0); context.lineTo(x, height); }
      for (let y = 0; y <= height; y += spacing) { context.moveTo(0, y); context.lineTo(width, y); }
      context.strokeStyle = 'rgba(255,255,255,0.035)';
      context.lineWidth = 0.5;
      context.stroke();
      if (!pointer) return;

      const glow = context.createRadialGradient(pointer.x, pointer.y, 0, pointer.x, pointer.y, radius + spacing);
      glow.addColorStop(0, 'rgba(188,23,31,0.085)');
      glow.addColorStop(0.5, 'rgba(188,23,31,0.025)');
      glow.addColorStop(1, 'rgba(188,23,31,0)');
      context.fillStyle = glow;
      context.fillRect(pointer.x - radius - spacing, pointer.y - radius - spacing, (radius + spacing) * 2, (radius + spacing) * 2);

      const left = Math.max(0, Math.floor((pointer.x - radius) / spacing) * spacing);
      const top = Math.max(0, Math.floor((pointer.y - radius) / spacing) * spacing);
      for (let x = left; x <= Math.min(width, pointer.x + radius); x += spacing) {
        for (let y = top; y <= Math.min(height, pointer.y + radius); y += spacing) {
          const intensity = Math.max(0, 1 - Math.hypot(x - pointer.x, y - pointer.y) / radius);
          if (intensity === 0) continue;
          context.beginPath();
          context.moveTo(x, y); context.lineTo(x + spacing, y);
          context.moveTo(x, y); context.lineTo(x, y + spacing);
          context.strokeStyle = `rgba(255,255,255,${intensity * 0.18})`;
          context.stroke();
          context.beginPath();
          context.arc(x, y, 1 + intensity * 1.5, 0, Math.PI * 2);
          context.fillStyle = `rgba(204,20,28,${intensity * 0.4})`;
          context.fill();
        }
      }
    }

    function schedule() {
      if (frame === null && !document.hidden) frame = requestAnimationFrame(draw);
    }

    function resize() {
      width = window.innerWidth;
      height = window.innerHeight;
      const ratio = Math.min(window.devicePixelRatio || 1, 2);
      canvas.width = Math.round(width * ratio);
      canvas.height = Math.round(height * ratio);
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
      schedule();
    }

    window.addEventListener('pointermove', event => {
      if (event.pointerType === 'touch' || !finePointer.matches) return;
      pointer = { x: event.clientX, y: event.clientY };
      schedule();
    }, { passive: true });
    window.addEventListener('pointerout', event => {
      if (!event.relatedTarget) { pointer = null; schedule(); }
    });
    window.addEventListener('resize', resize, { passive: true });
    window.addEventListener('blur', () => { pointer = null; schedule(); });
    finePointer.addEventListener('change', () => { pointer = null; schedule(); });
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        if (frame !== null) cancelAnimationFrame(frame);
        frame = null;
      } else schedule();
    });
    document.body.classList.add('grid-ready');
    resize();
  }

  if (!reducedMotion.matches) document.body.classList.add('effects-ready');
  prepareText();
  prepareReveals();
  prepareGrid();
  reducedMotion.addEventListener('change', event => {
    document.body.classList.toggle('effects-ready', !event.matches);
    if (event.matches) {
      [...typing.keys()].forEach(element => stopTyping(element));
      document.querySelectorAll('.scroll-reveal').forEach(section => section.classList.add('revealed'));
    }
  });
})();
