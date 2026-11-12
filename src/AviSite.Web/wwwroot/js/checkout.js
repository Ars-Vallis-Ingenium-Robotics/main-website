// Pay page: wires PayPal buttons (or the dev-only fake button) to our server.
// The browser never sends prices. The server builds the PayPal order from the stored order.
(() => {
  const root = document.getElementById("pay");
  if (!root) return;

  const orderId = root.dataset.orderId;
  const mode = root.dataset.mode;
  const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ?? "";
  const status = document.getElementById("pay-status");

  const say = (msg, isError = false) => {
    status.textContent = msg;
    status.classList.toggle("pay__status--error", isError);
  };

  async function post(path, body) {
    const res = await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json", RequestVerificationToken: token },
      body: JSON.stringify(body ?? {}),
      credentials: "same-origin",
    });
    let data = {};
    try { data = await res.json(); } catch { /* empty body */ }
    return { ok: res.ok, status: res.status, data };
  }

  async function createOrder() {
    say("");
    const r = await post(`/api/paypal/orders/${encodeURIComponent(orderId)}/create`);
    if (r.data.redirect) { window.location.assign(r.data.redirect); throw new Error("settled"); }
    if (!r.ok) {
      say(r.data.error || r.data.detail || "Couldn't start the payment. Please try again.", true);
      throw new Error("create failed");
    }
    return r.data.id;
  }

  async function capture(paypalOrderId, actions) {
    say("Confirming your payment…");
    const r = await post(`/api/paypal/orders/${encodeURIComponent(orderId)}/capture`, { paypalOrderId });
    if (r.status === 422 && r.data.retry && actions?.restart) {
      say(r.data.error, true);
      return actions.restart();
    }
    if (r.ok && r.data.redirect) { window.location.assign(r.data.redirect); return; }
    say(r.data.error || r.data.detail || "We couldn't confirm the payment. Don't pay again. Refresh in a minute.", true);
  }

  if (mode === "fake") {
    const btn = document.getElementById("fake-pay");
    btn?.addEventListener("click", async () => {
      btn.disabled = true;
      try { await capture(await createOrder()); }
      catch { /* message already shown */ }
      finally { btn.disabled = false; }
    });
    return;
  }

  if (!window.paypal?.Buttons) {
    say("PayPal didn't load. Check your connection or disable content blockers for this page, then refresh.", true);
    return;
  }

  window.paypal.Buttons({
    style: { layout: "vertical", shape: "rect", label: "pay", height: 48 },
    createOrder,
    onApprove: (data, actions) => capture(data.orderID, actions),
    onCancel: () => say("Payment cancelled. Your order is saved. Pay whenever you're ready."),
    onError: () => say("PayPal hit an error. You have not been charged. Please try again.", true),
  }).render("#paypal-buttons");
})();
