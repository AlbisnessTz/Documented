window.DocumentedAuth = (() => {
  const $ = (s, root = document) => root.querySelector(s);
  const setMessage = (text, kind = "") => {
    const el = $("#authMessage");
    el.textContent = text;
    el.className = "message " + kind;
  };

  function setMode(mode) {
    const register = mode === "register";
    $("#registerForm").classList.toggle("hidden", !register);
    $("#loginForm").classList.toggle("hidden", register);
    $("#registerTab").classList.toggle("active", register);
    $("#loginTab").classList.toggle("active", !register);
    $("#authTitle").textContent = register ? "Create your business account." : "Welcome back.";
    $("#authSubtitle").textContent = register
      ? "Your business gets its own workspace, settings and documents."
      : "Login to manage your business documents.";
    setMessage("");
  }

  async function send(url, body) {
    const response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });

    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(data.error || "Request failed.");
    return data;
  }

  async function start() {
    $("#registerTab").addEventListener("click", () => setMode("register"));
    $("#loginTab").addEventListener("click", () => setMode("login"));

    $("#registerForm").addEventListener("submit", async e => {
      e.preventDefault();
      const data = Object.fromEntries(new FormData(e.currentTarget).entries());
      setMessage("Creating your business workspace...");
      try {
        await send("/api/auth/register", data);
        window.location.href = "/";
      } catch (error) {
        setMessage(error.message, "error");
      }
    });

    $("#loginForm").addEventListener("submit", async e => {
      e.preventDefault();
      const data = Object.fromEntries(new FormData(e.currentTarget).entries());
      setMessage("Signing in...");
      try {
        await send("/api/auth/login", data);
        window.location.href = "/";
      } catch (error) {
        setMessage(error.message, "error");
      }
    });
  }

  return { start };
})();
