"use strict";
let job = null;
let status = { active: false, message: "准备就绪", done: 0, total: 0 };
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const segmenter = new Intl.Segmenter("zh-CN", { granularity: "grapheme" });
function state(j, message, error = false) {
  if (job !== j) return;
  status = { active: true, message, error, done: j.done, total: j.chars.length };
}
async function notify(j, message) {
  try { return await chrome.tabs.sendMessage(j.tabId, { jobId: j.id, ...message }, { frameId: 0 }); }
  catch { return null; }
}
function cancel(j, message) {
  if (!j || job !== j || j.canceled) return;
  j.canceled = true;
  j.reason = message;
  if (!j.running) void finish(j, message);
}
async function finish(j, message, error = false) {
  if (j.finishing) return;
  j.finishing = true;
  clearTimeout(j.timeout);
  if (j.attached) {
    j.attached = false;
    try { await chrome.debugger.detach({ tabId: j.tabId }); } catch { /* Already detached. */ }
  }
  await notify(j, { type: "FINISH", message });
  j.chars.length = 0;
  if (job === j) {
    status = { active: false, message, error, done: j.done, total: j.total };
    job = null;
    await chrome.action.setBadgeText({ text: "" });
  }
}
async function arm(message) {
  if (job) throw new Error("已有输入任务，请先停止。");
  if (typeof message.text !== "string" || !message.text.length) throw new Error("请先填写正文。");
  if (message.text.length > 100000) throw new Error("正文最多 100,000 个 UTF-16 字符。");
  if (![25, 60, 150, 300].includes(message.interval) || ![1, 2, 5].includes(message.delay)) {
    throw new Error("速度或等待时间无效，请重新选择。");
  }
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id || !/^(https?:|file:)/.test(tab.url || "")) {
    throw new Error("请先打开普通网页；浏览器内部页、扩展商店和 PDF 阅读器不支持。");
  }
  // Recheck after the asynchronous tab query so simultaneous requests cannot overwrite a task.
  if (job) throw new Error("已有输入任务，请先停止。");
  const normalized = message.text.replace(/\r\n?/g, "\n");
  const chars = Array.from(segmenter.segment(normalized), (part) => part.segment);
  const j = { id: crypto.randomUUID(), tabId: tab.id, windowId: tab.windowId, chars, total: chars.length,
    interval: message.interval, delay: message.delay, done: 0, attached: false, running: false, canceled: false };
  job = j;
  state(j, "请点击网页中的目标输入框，Esc 取消。");
  try {
    await chrome.scripting.executeScript({ target: { tabId: tab.id, frameIds: [0] }, files: ["content.js"] });
    if (j.canceled) throw new Error(j.reason);
    const ready = await notify(j, { type: "ARM" });
    if (!ready?.ok) throw new Error("无法在这个页面选择输入框，请刷新网页后再试。");
    await chrome.storage.session.set({ interval: j.interval, delay: j.delay });
    await chrome.action.setBadgeBackgroundColor({ color: "#5163d9" });
    await chrome.action.setBadgeText({ text: "待选" });
    j.timeout = setTimeout(() => cancel(j, "等待选框超时，请重新开始。"), 120000);
    return { ok: true };
  } catch (error) {
    await finish(j, error.message, true);
    throw error;
  }
}
async function check(j, nextLength = 0) {
  if (j.canceled || job !== j) throw new Error(j.reason || "输入已停止。");
  const [tab, win, target] = await Promise.all([
    chrome.tabs.get(j.tabId), chrome.windows.get(j.windowId), notify(j, { type: "CHECK", nextLength })
  ]);
  if (!tab.active || !win.focused || !target?.ok) {
    throw new Error(target?.reason || "目标失去焦点，输入已停止。");
  }
}
async function type(j, info) {
  j.running = true;
  clearTimeout(j.timeout);
  try {
    if (info.singleLine && j.chars.includes("\n")) throw new Error("正文包含换行，请选择多行文本框或编辑区。");
    await check(j);
    await chrome.debugger.attach({ tabId: j.tabId }, "1.3");
    j.attached = true;
    for (let left = j.delay; left > 0; left--) {
      await check(j);
      const message = `${left} 秒后开始 · Esc 停止`;
      state(j, message);
      await notify(j, { type: "PROGRESS", message });
      await sleep(1000);
    }
    await chrome.action.setBadgeText({ text: "输入" });
    for (const char of j.chars) {
      await check(j, char.length);
      // Browser input, not clipboard paste or a synthetic DOM event. Newlines do not press Enter.
      await chrome.debugger.sendCommand({ tabId: j.tabId }, "Input.insertText", { text: char });
      j.done++;
      const message = `正在输入 ${j.done} / ${j.total} · Esc 停止`;
      state(j, message);
      if (j.done === 1 || j.done % 5 === 0 || j.done === j.total) {
        await notify(j, { type: "PROGRESS", message });
      }
      await sleep(j.interval);
    }
    if (j.canceled) throw new Error(j.reason);
    await finish(j, `输入完成，共 ${j.done} 字。请检查内容后手动提交。`);
  } catch (error) {
    await finish(j, j.reason || `已停止（${j.done}/${j.total}）：${error.message}`, !j.canceled);
  }
}
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  const fromPopup = !sender.tab && sender.url === chrome.runtime.getURL("popup.html");
  if (fromPopup && message.type === "STATUS") { respond(status); return; }
  if (fromPopup && message.type === "ARM") {
    arm(message).then(respond, (error) => respond({ ok: false, error: error.message }));
    return true;
  }
  if (fromPopup && message.type === "STOP") {
    cancel(job, "用户停止输入。"); respond({ ok: true }); return;
  }
  const j = job;
  if (!j || sender.tab?.id !== j.tabId || sender.frameId !== 0 || message.jobId !== j.id) return;
  if (message.type === "SELECT" && !j.running && !j.canceled) {
    void type(j, message); respond({ ok: true });
  } else if (message.type === "CANCEL") {
    cancel(j, message.reason || "用户停止输入。"); respond({ ok: true });
  } else if (message.type === "KEEPALIVE") { respond({ ok: true }); }
});
chrome.commands.onCommand.addListener((command) => {
  if (command === "stop-typing") cancel(job, "快捷键停止输入。");
});
chrome.debugger.onDetach.addListener((source) => {
  if (job?.tabId === source.tabId && job.attached) {
    job.attached = false;
    cancel(job, "浏览器调试连接已断开，输入已停止。");
  }
});
chrome.tabs.onActivated.addListener(({ tabId }) => {
  if (job && job.tabId !== tabId) cancel(job, "已切换标签页，输入已停止。");
});
chrome.tabs.onRemoved.addListener((tabId) => {
  if (job?.tabId === tabId) cancel(job, "目标标签页已关闭。");
});
chrome.tabs.onUpdated.addListener((tabId, change) => {
  if (job?.tabId === tabId && change.status === "loading") cancel(job, "页面发生跳转，输入已停止。");
});
chrome.windows.onFocusChanged.addListener((windowId) => {
  if (job?.running && windowId !== job.windowId) cancel(job, "浏览器窗口失去焦点，输入已停止。");
});
