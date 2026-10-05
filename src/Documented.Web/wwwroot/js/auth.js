window.DocumentedAuth = (() => {
  const $ = (s, root = document) => root.querySelector(s);
  const setMessage = (text, kind = "") => {
    const el = $("#authMessage");
    el.textContent = text;
    el.className = "message " + kind;
  };

  function setMode(mode) {
    const register = mode === "register";
    const login = mode === "login";
    const reset = mode === "reset";

    $("#registerForm").classList.toggle("hidden", !register);
    $("#loginForm").classList.toggle("hidden", !login);
    $("#resetForm").classList.toggle("hidden", !reset);
    $("#registerTab").classList.toggle("active", register);
    $("#loginTab").classList.toggle("active", login);
    $("#resetTab").classList.toggle("hidden", !reset);
    $("#resetTab").classList.toggle("active", reset);

    $("#authTitle").textContent = register
      ? "Create your business account."
      : reset
        ? "Reset your password."
        : "Welcome back.";

    $("#authSubtitle").textContent = register
      ? "Your business gets its own workspace, settings and documents."
      : reset
        ? "Use your recovery code to regain access without email."
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

  function showRecoveryCode(code, message = "") {
    if (!code) return;
    const notice = $("#recoveryNotice");
    $("#recoveryCode").textContent = code;
    notice.classList.remove("hidden");
    setMessage(message || "Save your recovery code somewhere safe.", "ok");
  }

  async function start() {
    $("#registerTab").addEventListener("click", () => setMode("register"));
    $("#loginTab").addEventListener("click", () => setMode("login"));
    $("#resetTab").addEventListener("click", () => setMode("reset"));

    $("#forgotPasswordButton")?.addEventListener("click", () => setMode("reset"));
    $("#backToLoginButton")?.addEventListener("click", () => setMode("login"));

    $("#copyRecoveryCode")?.addEventListener("click", async () => {
      const code = $("#recoveryCode").textContent.trim();
      try {
        await navigator.clipboard.writeText(code);
        setMessage("Recovery code copied. Store it somewhere private.", "ok");
      } catch {
        window.prompt("Copy your recovery code:", code);
      }
    });

    $("#registerForm").addEventListener("submit", async e => {
      e.preventDefault();
      const data = Object.fromEntries(new FormData(e.currentTarget).entries());
      setMessage("Creating your business workspace...");
      try {
        const result = await send("/api/auth/register", data);
        showRecoveryCode(result.recoveryCode, "Account created. Save your recovery code before continuing.");
        e.currentTarget.classList.add("hidden");

        const continueButton = document.createElement("button");
        continueButton.type = "button";
        continueButton.className = "primary";
        continueButton.textContent = "Continue to Documented";
        continueButton.addEventListener("click", () => { window.location.href = "/"; });
        e.currentTarget.parentElement.insertBefore(continueButton, e.currentTarget.nextSibling);
      } catch (error) {
        setMessage(error.message, "error");
      }
    });

    $("#loginForm").addEventListener("submit", async e => {
      e.preventDefault();
      const data = Object.fromEntries(new FormData(e.currentTarget).entries());
      setMessage("Signing in...");
      try {
        const result = await send("/api/auth/login", data);
        if (result.recoveryCode) {
          showRecoveryCode(result.recoveryCode, "Your account did not have a recovery code yet. Save this new code.");
          const continueButton = document.createElement("button");
          continueButton.type = "button";
          continueButton.className = "primary";
          continueButton.textContent = "Continue";
          continueButton.addEventListener("click", () => { window.location.href = "/"; });
          e.currentTarget.classList.add("hidden");
          e.currentTarget.parentElement.insertBefore(continueButton, e.currentTarget.nextSibling);
        } else {
          window.location.href = "/";
        }
      } catch (error) {
        setMessage(error.message, "error");
      }
    });

    $("#resetForm").addEventListener("submit", async e => {
      e.preventDefault();
      const data = Object.fromEntries(new FormData(e.currentTarget).entries());

      if (data.newPassword !== data.confirmPassword) {
        return setMessage("New password and confirmation do not match.", "error");
      }

      setMessage("Resetting your password...");
      try {
        const result = await send("/api/auth/reset-password", {
          email: data.email,
          recoveryCode: data.recoveryCode,
          newPassword: data.newPassword
        });

        e.currentTarget.reset();
        setMode("login");
        showRecoveryCode(result.recoveryCode, "Password reset successfully. Save your new recovery code, then log in.");
      } catch (error) {
        setMessage(error.message, "error");
      }
    });

    $("#changePasswordForm")?.addEventListener("submit", async e => {
      e.preventDefault();
      const data = Object.fromEntries(new FormData(e.currentTarget).entries());

      if (data.newPassword !== data.confirmPassword) {
        return setMessage("New password and confirmation do not match.", "error");
      }

      setMessage("Changing your password...");
      try {
        const result = await send("/api/auth/change-password", {
          currentPassword: data.currentPassword,
          newPassword: data.newPassword
        });

        e.currentTarget.reset();
        showRecoveryCode(result.recoveryCode, "Password changed. Save your new recovery code.");
      } catch (error) {
        setMessage(error.message, "error");
      }
    });

    $("#rotateRecoveryCode")?.addEventListener("click", async () => {
      setMessage("Generating a new recovery code...");
      try {
        const result = await send("/api/auth/recovery-code", {});
        showRecoveryCode(result.recoveryCode, "New recovery code generated. The old one is no longer valid.");
      } catch (error) {
        setMessage(error.message, "error");
      }
    });
  }

  return { start };
})();
