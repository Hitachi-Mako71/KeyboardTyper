"use strict";
window.observed = { paste: 0, input: 0, trusted: 0, submits: 0, lastType: "" };
function render() { document.getElementById("events").textContent = JSON.stringify(window.observed, null, 2); }
document.addEventListener("paste", (event) => { event.preventDefault(); window.observed.paste++; render(); }, true);
document.addEventListener("input", (event) => {
  window.observed.input++;
  if (event.isTrusted) window.observed.trusted++;
  window.observed.lastType = event.inputType;
  render();
});
document.getElementById("form").addEventListener("submit", (event) => {
  event.preventDefault(); window.observed.submits++; render();
});
