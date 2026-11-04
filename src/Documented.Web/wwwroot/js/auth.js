window.DocumentedAuth = (() => {
  const $ = (s, root = document) => root.querySelector(s);
  const setMessage = (text, kind = "") => {
    const el = $("#authMessage");
    el.textContent = text;
    el.className = "message " + kind;
  };

  const forms = {
    register: "#registerForm",
    login: "#loginForm",
    forgot: "#forgotForm",
    reset: "#resetForm"
  };

  const titles = {
    register: ["Create your business account.", "Your business gets its own workspace, settings and documents."],
    login: ["Welcome back.", "Login to manage your business documents."],
    forgot: ["Reset your password.", "Enter your account email and we will send you a reset link."],
    reset: ["Choose a new password.", "Use at least 8 characters."]
  };

  let resetToken = "";

  function setMode(mode) {
    for (const [key, selector] of Object.entries(forms))
      $(selector).classList.toggle("hidden", key !== mode);

    const showTabs = mode === "register" || mode === "login";
    $(".auth-tabs").classList.toggle("hidden", !showTabs);
    $("#registerTab").classList.toggle("active", mode === "register");
    $("#loginTab").classList.toggle("active", mode === "login");

    $("#authTitle").textContent = titles[mode][0];
    $("#authSubtitle").textContent = titles[mode][1];
    setMessage("");
  }

  async function send(url, body) {
    const response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });

    const data = await response.json().catch(() => ({}));
    if (response.status === 429) throw new Error("Too many attempts. Please wait a minute and try again.");
    if (!response.ok) throw new Error(data.error || "Request failed.");
    return data;
  }

  async function start() {
    $("#registerTab").addEventListener("click", () => setMode("register"));
    $("#loginTab").addEventListener("click", () => setMode("login"));
    $("#forgotLink").addEventListener("click", () => setMode("forgot"));
    $("#backToLoginFromForgot").addEventListener("click", () => setMode("login"));

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

    $("#forgotForm").addEventListener("submit", async e => {
      e.preventDefault();
      const data = Object.fromEntries(new FormData(e.currentTarget).entries());
      setMessage("Sending reset link...");
      try {
        const result = await send("/api/auth/forgot", { email: data.email });
        setMessage(result.message, "ok");
      } catch (error) {
        setMessage(error.message, "error");
      }
    });

    $("#resetForm").addEventListener("submit", async e => {
      e.preventDefault();
      const data = Object.fromEntries(new FormData(e.currentTarget).entries());
      if (data.password !== data.confirm) {
        setMessage("The two passwords do not match.", "error");
        return;
      }

      setMessage("Saving your new password...");
      try {
        const result = await send("/api/auth/reset", { token: resetToken, password: data.password });
        history.replaceState(null, "", "/Account");
        resetToken = "";
        e.currentTarget.reset();
        setMode("login");
        setMessage(result.message, "ok");
      } catch (error) {
        setMessage(error.message, "error");
      }
    });

    const token = new URLSearchParams(window.location.search).get("reset");
    if (token) {
      resetToken = token;
      setMode("reset");
    }
  }

  return { start };
})();
