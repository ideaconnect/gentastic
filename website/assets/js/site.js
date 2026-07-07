(() => {
  "use strict";

  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  /* Sticky nav: shadow/opaque background once the page scrolls, mobile menu toggle. */
  const nav = document.getElementById("site-nav");
  if (nav) {
    const onScroll = () => nav.classList.toggle("is-scrolled", window.scrollY > 8);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });

    const toggle = document.getElementById("nav-toggle");
    if (toggle) {
      toggle.addEventListener("click", () => {
        const open = nav.classList.toggle("is-open");
        toggle.setAttribute("aria-expanded", String(open));
      });
      nav.querySelectorAll(".nav-mobile a").forEach((a) =>
        a.addEventListener("click", () => {
          nav.classList.remove("is-open");
          toggle.setAttribute("aria-expanded", "false");
        })
      );
    }
  }

  /* Scroll-reveal: fade+rise elements into view once, via IntersectionObserver. */
  const revealEls = document.querySelectorAll(".reveal");
  if (revealEls.length) {
    if (reducedMotion || !("IntersectionObserver" in window)) {
      revealEls.forEach((el) => el.classList.add("in-view"));
    } else {
      const io = new IntersectionObserver(
        (entries) => {
          entries.forEach((entry) => {
            if (entry.isIntersecting) {
              entry.target.classList.add("in-view");
              io.unobserve(entry.target);
            }
          });
        },
        { threshold: 0.15, rootMargin: "0px 0px -40px 0px" }
      );
      revealEls.forEach((el) => io.observe(el));
    }
  }

  /* Hero sparkles: a handful of randomly-placed twinkling dots. Skipped under reduced motion. */
  const sparkHost = document.querySelector(".hero-sparkles");
  if (sparkHost && !reducedMotion) {
    const count = 22;
    const frag = document.createDocumentFragment();
    for (let i = 0; i < count; i++) {
      const s = document.createElement("span");
      s.className = "spark";
      s.style.left = `${Math.random() * 100}%`;
      s.style.top = `${Math.random() * 100}%`;
      s.style.animationDelay = `${(Math.random() * 4.5).toFixed(2)}s`;
      s.style.animationDuration = `${(3.5 + Math.random() * 3).toFixed(2)}s`;
      frag.appendChild(s);
    }
    sparkHost.appendChild(frag);
  }

  /* Lightbox for the example gallery: click (or Enter/Space) a thumbnail to view it large.
     Keyboard-accessible: focusable items, a real dialog role, a focus trap while open, and
     focus is returned to the thumbnail that opened it on close. */
  const lightbox = document.getElementById("lightbox");
  if (lightbox) {
    const lightboxImg = lightbox.querySelector("img");
    const lightboxCaption = lightbox.querySelector(".lightbox-caption");
    const closeBtn = lightbox.querySelector(".lightbox-close");
    let lastTrigger = null;

    const open = (item) => {
      lastTrigger = item;
      lightboxImg.src = item.dataset.full;
      lightboxImg.alt = item.dataset.alt || "";
      lightboxCaption.textContent = item.dataset.caption || "";
      lightbox.classList.add("is-open");
      document.body.style.overflow = "hidden";
      closeBtn.focus();
    };
    const close = () => {
      lightbox.classList.remove("is-open");
      lightboxImg.src = "";
      document.body.style.overflow = "";
      if (lastTrigger) lastTrigger.focus();
    };

    document.querySelectorAll(".gallery-item[data-full]").forEach((item) => {
      item.setAttribute("tabindex", "0");
      item.setAttribute("role", "button");
      if (!item.hasAttribute("aria-label")) {
        item.setAttribute("aria-label", `View larger: ${item.dataset.caption || item.dataset.alt || "image"}`);
      }
      item.addEventListener("click", () => open(item));
      item.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          open(item);
        }
      });
    });

    closeBtn.addEventListener("click", close);
    lightbox.addEventListener("click", (e) => {
      if (e.target === lightbox) close();
    });
    document.addEventListener("keydown", (e) => {
      if (!lightbox.classList.contains("is-open")) return;
      if (e.key === "Escape") {
        close();
      } else if (e.key === "Tab") {
        // Only the close button is meaningfully focusable inside - keep focus trapped on it.
        e.preventDefault();
        closeBtn.focus();
      }
    });
  }
})();
