(() => {
  "use strict";
  if (globalThis.__keyboardTyperInstalled) return;
  globalThis.__keyboardTyperInstalled = true;
  let session = null;
  const editable = (node) => {
    if (!(node instanceof HTMLElement) || node.closest("[inert]")) return null;
    if (node instanceof HTMLTextAreaElement) return !node.disabled && !node.readOnly ? node : null;
    if (node instanceof HTMLInputElement) {
      return ["text", "search", "email", "url", "tel"].includes(node.type) && !node.disabled && !node.readOnly ? node : null;
    }
    if (node.isContentEditable) {
      let root = node;
      while (root.parentElement?.isContentEditable) root = root.parentElement;
      return root;
    }
    return null;
  };
  function active() {
    let node = document.activeElement;
    while (node?.shadowRoot?.activeElement) node = node.shadowRoot.activeElement;
    return node;
  }
  function valid(nextLength = 0) {
    const s = session;
    if (!s?.target?.isConnected || editable(s.target) !== s.target) return { ok: false, reason: "目标输入框已移除或不可编辑。" };
    const focused = active();
    if (!document.hasFocus() || document.hidden || (focused !== s.target && !s.target.contains(focused))) {
      return { ok: false, reason: "光标离开目标输入框，输入已停止。" };
    }
    if (s.target.isContentEditable) {
      const selection = s.target.getRootNode().getSelection?.() || window.getSelection();
      if (!selection?.rangeCount || !s.target.contains(selection.anchorNode) || !s.target.contains(selection.focusNode)) {
        return { ok: false, reason: "编辑区光标无效，输入已停止。" };
      }
    }
    if (s.target instanceof HTMLInputElement || s.target instanceof HTMLTextAreaElement) {
      if (s.target.maxLength >= 0) {
        const selected = (s.target.selectionEnd || 0) - (s.target.selectionStart || 0);
        if (s.target.value.length - selected + nextLength > s.target.maxLength) return { ok: false, reason: "输入框已达到字数上限，无法容纳下一个字符。" };
      }
    }
    return { ok: true };
  }
  function send(type, extra = {}) {
    if (!session) return;
    chrome.runtime.sendMessage({ type, jobId: session.id, ...extra }).catch(() => cleanup());
  }
  function show(message) {
    if (session) session.label.textContent = message;
  }
  function cleanup() {
    if (!session) return;
    session.controller.abort();
    clearInterval(session.keepalive);
    clearTimeout(session.expiry);
    session.host.remove();
    session = null;
  }
  function arm(id) {
    cleanup();
    const controller = new AbortController();
    const host = document.createElement("div");
    host.style.cssText = "all:initial;position:fixed;bottom:24px;right:24px;z-index:2147483647;";
    const shadow = host.attachShadow({ mode: "closed" });
    const panel = document.createElement("div");
    panel.style.cssText = "display:flex;align-items:center;gap:18px;background:#202b45;color:#fff;padding:14px 18px;border-radius:12px;box-shadow:0 8px 32px #0003;font:14px/1.6 'Microsoft YaHei',sans-serif;max-width:480px";
    const label = document.createElement("span");
    label.setAttribute("role", "status");
    const button = document.createElement("button");
    button.textContent = "停止";
    button.style.cssText = "border:1px solid #ffffff55;background:#ffffff18;color:white;border-radius:7px;padding:5px 12px;cursor:pointer;font:inherit;white-space:nowrap";
    button.addEventListener("pointerdown", (e) => e.preventDefault());
    button.addEventListener("click", () => send("CANCEL"));
    panel.append(label, button); shadow.append(panel); document.documentElement.append(host);
    session = { id, target: null, host, label, controller };
    const opts = { capture: true, signal: controller.signal };
    show("逐字输入：请点击目标输入框 · Esc 取消");
    document.addEventListener("click", (e) => {
      if (e.composedPath().includes(host)) return;
      if (session.target) { send("CANCEL", { reason: "点击了页面，输入已停止。" }); return; }
      const node = e.composedPath().map(editable).find(Boolean);
      if (!node) { show("请选择普通输入框、多行文本框或可编辑区域（不支持 iframe）。"); return; }
      session.target = node;
      // Let the page finish placing the caret before the background starts its countdown.
      const current = session;
      setTimeout(() => {
        if (session !== current) return;
        if (active() !== node && !node.contains(active())) node.focus({ preventScroll: true });
        send("SELECT", { singleLine: node instanceof HTMLInputElement });
      }, 0);
    }, opts);
    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape") {
        e.preventDefault(); e.stopImmediatePropagation(); send("CANCEL");
      } else if (session?.target && e.isTrusted && !["Shift", "Control", "Alt", "Meta"].includes(e.key)) {
        send("CANCEL", { reason: "检测到手动按键，自动输入已停止。" });
      }
    }, opts);
    document.addEventListener("focusin", (e) => {
      if (session?.target && !e.composedPath().includes(host) && editable(active()) !== session.target) {
        send("CANCEL", { reason: "光标离开目标输入框，输入已停止。" });
      }
    }, opts);
    document.addEventListener("visibilitychange", () => {
      if (document.hidden) send("CANCEL", { reason: "页面已隐藏，输入已停止。" });
    }, { signal: controller.signal });
    session.keepalive = setInterval(() => send("KEEPALIVE"), 15000);
    session.expiry = setTimeout(() => {
      if (session && !session.target) send("CANCEL", { reason: "等待选框超时。" });
    }, 120000);
  }
  chrome.runtime.onMessage.addListener((message, _sender, respond) => {
    if (message.type === "ARM") { arm(message.jobId); respond({ ok: true }); return; }
    if (!session || message.jobId !== session.id) return;
    if (message.type === "CHECK") respond(valid(message.nextLength));
    else if (message.type === "PROGRESS") { show(message.message); respond({ ok: true }); }
    else if (message.type === "FINISH") {
      const finalMessage = message.message;
      const old = session;
      show(finalMessage);
      old.controller.abort(); clearInterval(old.keepalive); clearTimeout(old.expiry);
      session = null;
      setTimeout(() => old.host.remove(), 4000);
      respond({ ok: true });
    }
  });
})();
