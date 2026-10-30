(() => {
  const script = document.currentScript;
  const key = script && script.dataset.widgetKey;
  if (!key) return;
  const api = new URL(script.src).origin;
  const host = document.createElement('div');
  host.style.cssText = 'position:fixed;right:20px;bottom:20px;z-index:2147483000';
  document.body.appendChild(host);
  const shadow = host.attachShadow({ mode: 'open' });
  const style = document.createElement('style');
  style.textContent = '*{box-sizing:border-box;font:14px/1.45 Inter,Arial,sans-serif}button,input{font:inherit}.toggle{border:0;border-radius:50px;background:#16233b;color:#fff;padding:13px 18px;box-shadow:0 8px 30px #14233b33;cursor:pointer;font-weight:700}.panel{display:none;width:min(360px,calc(100vw - 32px));height:min(540px,calc(100vh - 110px));margin-bottom:10px;background:#fff;border:1px solid #e3e8f1;border-radius:16px;box-shadow:0 14px 55px #14233b2b;overflow:hidden;color:#17243d}.panel.open{display:flex;flex-direction:column}.head{padding:16px;background:#16233b;color:#fff}.head b{display:block;font-size:15px}.head small{color:#c4cee0}.messages{flex:1;overflow:auto;padding:12px;background:#f6f8fb}.message{max-width:90%;padding:10px 12px;margin:7px 0;border-radius:12px;background:#fff;white-space:pre-wrap;overflow-wrap:anywhere;box-shadow:0 1px 3px #12203b12}.message.customer{margin-left:auto;background:#eaf0ff}form{display:grid;gap:7px;padding:10px;border-top:1px solid #e9edf3;background:#fff}.message-input{display:flex;gap:7px}.message-input input,.lead input{min-width:0;width:100%;padding:10px;border:1px solid #dce3ee;border-radius:9px;outline-color:#3157c9}.message-input button,.escalate{border:0;border-radius:9px;background:#3157c9;color:#fff;padding:0 14px;font-weight:700;cursor:pointer}.consent{display:flex;align-items:center;gap:6px;color:#68758b;font-size:11px}.lead{display:none;grid-template-columns:1fr 1fr;gap:6px}.lead.show{display:grid}.lead input{font-size:12px;padding:8px}.escalate{justify-self:start;background:#eef2fa;color:#33445f;padding:7px 10px;font-size:11px}.notice{min-height:14px;margin:0;color:#9c4b4b;font-size:11px}';
  shadow.appendChild(style);
  shadow.innerHTML += '<section class="panel" aria-label="Customer support chat"><header class="head"><b class="title">BusinessPilot support</b><small>Ask a question or request team follow-up</small></header><div class="messages" aria-live="polite"></div><form><div class="lead"><input name="name" maxlength="120" placeholder="Your name"><input name="email" type="email" maxlength="254" placeholder="Email for follow-up"></div><label class="consent"><input name="consent" type="checkbox"> You may contact me about this request</label><div class="message-input"><input name="message" maxlength="4000" required placeholder="Write a message…"><button type="submit">Send</button></div><button class="escalate" type="button" hidden>Request team follow-up</button><p class="notice" role="status"></p></form></section><button class="toggle" type="button" aria-expanded="false">Chat with us</button>';
  const panel = shadow.querySelector('.panel');
  const toggle = shadow.querySelector('.toggle');
  const messages = shadow.querySelector('.messages');
  const form = shadow.querySelector('form');
  const notice = shadow.querySelector('.notice');
  const sendButton = shadow.querySelector('.message-input button');
  const escalate = shadow.querySelector('.escalate');
  const storageKey = 'businesspilot-support-' + key;
  let conversationId = sessionStorage.getItem(storageKey);
  let visitorKey = sessionStorage.getItem(storageKey + '-visitor');
  if (!visitorKey) {
    visitorKey = crypto.randomUUID();
    sessionStorage.setItem(storageKey + '-visitor', visitorKey);
  }
  const addMessage = (role, text) => {
    const bubble = document.createElement('div');
    bubble.className = 'message' + (role === 'Customer' ? ' customer' : '');
    bubble.textContent = text;
    messages.appendChild(bubble);
    messages.scrollTop = messages.scrollHeight;
  };
  const showConversation = data => {
    conversationId = data.id || data.Id;
    sessionStorage.setItem(storageKey, conversationId);
    messages.replaceChildren();
    for (const item of data.messages || data.Messages || []) addMessage(item.role || item.Role, item.content || item.Content);
    const status = data.status || data.Status;
    escalate.hidden = !conversationId || status === 'Escalated' || status === 'Closed';
  };
  toggle.addEventListener('click', async () => {
    panel.classList.toggle('open');
    toggle.setAttribute('aria-expanded', String(panel.classList.contains('open')));
    if (!panel.classList.contains('open')) return;
    try {
      const response = await fetch(api + '/api/support/widget/' + encodeURIComponent(key));
      if (!response.ok) throw new Error('This support widget is unavailable.');
      const info = await response.json();
      shadow.querySelector('.title').textContent = info.organizationName + ' support';
      if (conversationId) {
        const history = await fetch(api + '/api/support/widget/' + encodeURIComponent(key) + '/conversations/' + encodeURIComponent(conversationId));
        if (history.ok) showConversation(await history.json());
      }
      if (!messages.childElementCount) addMessage('System', 'Hello! Ask a question about this business.');
    } catch (error) { notice.textContent = error.message; }
  });
  shadow.querySelector('[name="consent"]').addEventListener('change', event => {
    shadow.querySelector('.lead').classList.toggle('show', event.target.checked);
    shadow.querySelector('[name="name"]').required = event.target.checked;
    shadow.querySelector('[name="email"]').required = event.target.checked;
  });
  form.addEventListener('submit', async event => {
    event.preventDefault();
    const consent = form.elements.consent.checked;
    const body = {
      conversationId: conversationId,
      message: form.elements.message.value.trim(),
      visitorKey: visitorKey,
      consentToContact: consent,
      name: consent ? form.elements.name.value.trim() : null,
      email: consent ? form.elements.email.value.trim() : null
    };
    if (!body.message) return;
    sendButton.disabled = true;
    notice.textContent = '';
    try {
      const response = await fetch(api + '/api/support/widget/' + encodeURIComponent(key) + '/messages', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.message || 'Message could not be sent.');
      showConversation(result.conversation || result.Conversation);
      form.elements.message.value = '';
    } catch (error) { notice.textContent = error.message; }
    finally { sendButton.disabled = false; }
  });
  escalate.addEventListener('click', async () => {
    if (!conversationId) return;
    escalate.disabled = true;
    try {
      const response = await fetch(api + '/api/support/widget/' + encodeURIComponent(key) + '/conversations/' + encodeURIComponent(conversationId) + '/escalate', { method: 'POST' });
      if (!response.ok) throw new Error('Team follow-up could not be requested.');
      addMessage('System', 'Your request is marked for team follow-up. Contact details are included only if you consented.');
      escalate.hidden = true;
    } catch (error) { notice.textContent = error.message; }
    finally { escalate.disabled = false; }
  });
})();
