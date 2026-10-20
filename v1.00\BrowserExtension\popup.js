"use strict";
const $ = (id) => document.getElementById(id);
const text = $("text");
let saveTimer;
let busy = false;
let starting = false;
let localError = "";
function count() {
  $("count").textContent = `${Array.from(text.value).length.toLocaleString()} 字`;
  $("start").disabled = busy || starting || !text.value.length;
}
function paint(state) {
  busy = state.active;
  $("status").textContent = !state.active && localError ? localError : state.message;
  $("status").className = state.error || (!state.active && localError) ? "error" : "";
  $("stop").disabled = !busy;
  text.disabled = busy;
  $("interval").disabled = busy;
  $("delay").disabled = busy;
  $("progress").hidden = !state.total;
  $("progress").max = state.total || 1;
  $("progress").value = state.done || 0;
  count();
}
async function refresh() {
  try { paint(await chrome.runtime.sendMessage({ type: "STATUS" })); }
  catch { $("status").textContent = "扩展连接已断开，请重新打开。"; }
}
text.addEventListener("input", () => {
  localError = "";
  count();
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => chrome.storage.session.set({ draft: text.value }), 200);
});
$("start").addEventListener("click", async () => {
  clearTimeout(saveTimer);
  starting = true;
  localError = "";
  count();
  try {
    const response = await chrome.runtime.sendMessage({
      type: "ARM", text: text.value, interval: Number($("interval").value), delay: Number($("delay").value)
    });
    if (!response.ok) throw new Error(response.error);
    await chrome.storage.session.remove("draft");
    window.close();
  } catch (error) {
    localError = error.message;
    $("status").textContent = error.message;
    $("status").className = "error";
  } finally { starting = false; count(); }
});
$("stop").addEventListener("click", async () => {
  await chrome.runtime.sendMessage({ type: "STOP" });
  await refresh();
});
async function init() {
  const saved = await chrome.storage.session.get(["draft", "interval", "delay"]);
  text.value = saved.draft || "";
  if (saved.interval) $("interval").value = String(saved.interval);
  if (saved.delay) $("delay").value = String(saved.delay);
  count();
  await refresh();
  setInterval(refresh, 500);
}
init().catch((error) => { $("status").textContent = error.message; });
