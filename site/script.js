"use strict";

document.documentElement.classList.add("js");

const menuToggle = document.querySelector(".menu-toggle");
const navigation = document.querySelector("#navigation");

menuToggle.hidden = false;
menuToggle.addEventListener("click", () => {
  const isOpen = menuToggle.getAttribute("aria-expanded") !== "true";
  menuToggle.setAttribute("aria-expanded", String(isOpen));
  navigation.classList.toggle("is-open", isOpen);
  menuToggle.querySelector("span").textContent = isOpen ? "\u2212" : "+";
});

function closeMenu() {
  menuToggle.setAttribute("aria-expanded", "false");
  navigation.classList.remove("is-open");
  menuToggle.querySelector("span").textContent = "+";
}

navigation.addEventListener("click", (event) => {
  if (event.target.closest("a")) closeMenu();
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && menuToggle.getAttribute("aria-expanded") === "true") {
    closeMenu();
    menuToggle.focus();
  }
});

function revealGuide(hash) {
  const guide = document.getElementById(hash.slice(1));
  if (guide instanceof HTMLDetailsElement) {
    guide.open = true;
    guide.scrollIntoView({ block: "start" });
  }
}

document.addEventListener("click", (event) => {
  const link = event.target.closest('a[href^="#guide-"]');
  if (link) revealGuide(link.getAttribute("href"));
});

window.addEventListener("hashchange", () => revealGuide(window.location.hash));
revealGuide(window.location.hash);

if (navigator.clipboard && window.isSecureContext) {
  const copyStatus = document.querySelector("#copy-status");
  document.querySelectorAll(".code-block").forEach((block, index) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "copy-button";
    button.textContent = "Copy";
    button.setAttribute("aria-label", `Copy code example ${index + 1}`);
    block.append(button);

    button.addEventListener("click", async () => {
      try {
        await navigator.clipboard.writeText(block.querySelector("code").textContent);
        button.textContent = "Copied!";
        copyStatus.textContent = "Code copied to clipboard.";
      } catch (error) {
        button.textContent = "Select code";
        copyStatus.textContent = "Clipboard access was denied. Select and copy the code manually.";
        const selection = window.getSelection();
        const range = document.createRange();
        range.selectNodeContents(block.querySelector("code"));
        selection.removeAllRanges();
        selection.addRange(range);
      }
    });
  });
}
