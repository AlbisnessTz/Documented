window.DocumentedApp = (() => {
  const $ = (s, root = document) => root.querySelector(s);
  const $$ = (s, root = document) => [...root.querySelectorAll(s)];
  const money = n => Number(n || 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  function setMessage(id, text, kind = "") {
    const el = $(id);
    if (!el) return;
    el.textContent = text;
    el.className = "message " + kind;
  }

  function connectionStatus() {
    const dot = $("#connectionDot"), text = $("#connectionText");
    if (!dot || !text) return;
    const online = navigator.onLine;
    dot.className = "status-dot " + (online ? "online" : "offline");
    text.textContent = online ? "Online" : "Offline";
  }

  function createItem(description = "", quantity = 1, unitPrice = 0) {
    const row = document.createElement("div");
    row.className = "item-row";
    row.innerHTML = `
      <input class="item-description" placeholder="Product / service" value="${escapeHtml(description)}" />
      <input class="item-qty" type="number" min="0.001" step="0.001" value="${quantity}" />
      <input class="item-price" type="number" min="0" step="0.01" value="${unitPrice}" />
      <strong class="item-total">0.00</strong>
      <button type="button" class="remove-item" title="Remove">×</button>`;
    row.querySelectorAll("input").forEach(x => x.addEventListener("input", recalculate));
    row.querySelector(".remove-item").addEventListener("click", () => { row.remove(); recalculate(); });
    $("#items").appendChild(row);
    recalculate();
  }

  function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]));
  }

  function collectItems() {
    return $$(".item-row").map(row => ({
      description: $(".item-description", row).value.trim(),
      quantity: Number($(".item-qty", row).value),
      unitPrice: Number($(".item-price", row).value)
    })).filter(x => x.description && x.quantity > 0 && x.unitPrice >= 0);
  }

  function recalculate() {
    let subtotal = 0;
    $$(".item-row").forEach(row => {
      const qty = Number($(".item-qty", row).value || 0);
      const price = Number($(".item-price", row).value || 0);
      const total = qty * price;
      $(".item-total", row).textContent = money(total);
      subtotal += total;
    });
    const discount = Math.min(Math.max(Number($("#discount")?.value || 0), 0), subtotal);
    $("#subtotal").textContent = money(subtotal);
    $("#total").textContent = money(subtotal - discount);
  }

  async function loadSessionContext() {
    if (!navigator.onLine) return localStorage.getItem("documented.accountEmail") || "";
    try {
      const response = await fetch("/api/session", { cache: "no-store" });
      if (!response.ok) return "";
      const data = await response.json();
      const email = data.email || "";
      if (email) localStorage.setItem("documented.accountEmail", email);
      return email;
    } catch {
      return localStorage.getItem("documented.accountEmail") || "";
    }
  }

  async function saveOffline(payload) {
    const key = "documented.offline.queue";
    const queue = JSON.parse(localStorage.getItem(key) || "[]");
    const accountEmail = localStorage.getItem("documented.accountEmail") || "";
    queue.push({ ...payload, accountEmail, localId: crypto.randomUUID(), savedAt: new Date().toISOString() });
    localStorage.setItem(key, JSON.stringify(queue));
  }

  async function syncOfflineQueue() {
    if (!navigator.onLine) return;
    const key = "documented.offline.queue";
    const queue = JSON.parse(localStorage.getItem(key) || "[]");
    if (!queue.length) return;
    const accountEmail = await loadSessionContext();
    if (!accountEmail) return;

    const remaining = [];
    for (const payload of queue) {
      if (payload.accountEmail && payload.accountEmail !== accountEmail) {
        remaining.push(payload);
        continue;
      }
      try {
        const response = await fetch("/api/documents", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        });
        if (!response.ok) remaining.push(payload);
      } catch { remaining.push(payload); }
    }
    localStorage.setItem(key, JSON.stringify(remaining));
    if (!remaining.length) window.dispatchEvent(new Event("documented:synced"));
  }

  async function start() {
    connectionStatus();
    await loadSessionContext();
    window.addEventListener("online", () => { connectionStatus(); syncOfflineQueue(); loadDocuments(); });
    window.addEventListener("offline", connectionStatus);

    $("#addItem")?.addEventListener("click", () => createItem());
    $("#discount")?.addEventListener("input", recalculate);
    $("#clearDraft")?.addEventListener("click", () => {
      $("#documentForm").reset();
      $("#items").innerHTML = "";
      createItem();
      recalculate();
    });
    $("#refreshDocs")?.addEventListener("click", loadDocuments);
    $("#documentForm")?.addEventListener("submit", submitDocument);

    createItem();
    await syncOfflineQueue();
    await loadDocuments();
    registerServiceWorker();
  }

  async function submitDocument(event) {
    event.preventDefault();
    const items = collectItems();
    if (!items.length) return setMessage("#formMessage", "Add at least one item.", "error");

    const payload = {
      documentType: "Proforma",
      customerName: $("[name=customerName]").value.trim(),
      customerPhone: $("[name=customerPhone]").value.trim(),
      customerEmail: $("[name=customerEmail]").value.trim(),
      customerAddress: $("[name=customerAddress]").value.trim(),
      notes: $("#notes").value.trim(),
      discount: Number($("#discount").value || 0),
      items
    };

    if (!payload.customerName) return setMessage("#formMessage", "Customer name is required.", "error");

    if (!navigator.onLine) {
      await saveOffline(payload);
      setMessage("#formMessage", "Saved on this device. It will sync when you are online.", "ok");
      return;
    }

    setMessage("#formMessage", "Creating document...");
    try {
      const response = await fetch("/api/documents", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Could not create document.");
      setMessage("#formMessage", `Created ${data.number}. Opening shareable document...`, "ok");
      window.open("/d/" + data.publicToken, "_blank");
      $("#clearDraft").click();
      await loadDocuments();
    } catch (error) {
      await saveOffline(payload);
      setMessage("#formMessage", "Server unavailable. Saved locally for sync.", "ok");
    }
  }

  async function loadDocuments() {
    const target = $("#documentsList");
    if (!target) return;
    if (!navigator.onLine) {
      target.innerHTML = '<div class="empty">You are offline. New documents will be queued and synced later.</div>';
      return;
    }
    try {
      const response = await fetch("/api/documents?limit=25");
      if (!response.ok) throw new Error();
      const docs = await response.json();
      target.innerHTML = docs.length ? docs.map(doc => `
        <a class="document-card" href="/d/${doc.publicToken}" target="_blank">
          <div><strong>${escapeHtml(doc.number)}</strong><small>${escapeHtml(doc.customerName)}</small></div>
          <div class="amount">${money(doc.total)}<br><small>${new Date(doc.createdAtUtc).toLocaleDateString()}</small></div>
        </a>`).join("") : '<div class="empty">No documents yet.</div>';
    } catch {
      target.innerHTML = '<div class="empty">Could not load documents.</div>';
    }
  }

  async function setup() {
    try {
      const response = await fetch("/api/business");
      const data = await response.json();
      $$("[name]").forEach(input => { if (input.name in data) input.value = data[input.name] ?? ""; });
    } catch {}
    $("#businessForm")?.addEventListener("submit", async event => {
      event.preventDefault();
      const data = Object.fromEntries(new FormData(event.currentTarget).entries());
      try {
        const response = await fetch("/api/business", {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(data)
        });
        if (!response.ok) throw new Error();
        setMessage("#setupMessage", "Business settings saved.", "ok");
      } catch {
        setMessage("#setupMessage", "Business settings need an internet connection in this first version.", "error");
      }
    });
    registerServiceWorker();
  }

  function registerServiceWorker() {
    if ("serviceWorker" in navigator) navigator.serviceWorker.register("/sw.js").catch(() => {});
  }

  return { start, setup };
})();
