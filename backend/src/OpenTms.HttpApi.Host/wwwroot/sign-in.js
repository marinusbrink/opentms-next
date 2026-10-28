/* The sign-in page (docs/designs/62-sign-in-tenant-detection.md).
 * Step 2: the eye button shows or hides the password.
 * Step 3: while the user types part of the organisation's name, the server says whether exactly one of the
 * organisations their credentials are valid in contains it, and only then its name. No list is ever shown; Tab or the
 * right arrow at the end of the text takes the found name. The organisation chosen last time is filled in already. */
(() => {
  const passwordButton = document.getElementById("PasswordVisibilityButton");
  const password = document.getElementById("Password");
  if (passwordButton && password) {
    passwordButton.addEventListener("click", () => {
      const show = password.type === "password";
      password.type = show ? "text" : "password";
      passwordButton.querySelector("i")?.classList.toggle("fa-eye", show);
      passwordButton.querySelector("i")?.classList.toggle("fa-eye-slash", !show);
    });
  }

  const form = document.getElementById("SignInTenantForm");
  if (!form) {
    return;
  }

  const input = form.querySelector('input[name="TenantQuery"]');
  const hint = document.getElementById("SignInTenantHint");
  const token = form.querySelector('input[name="__RequestVerificationToken"]');
  const minLength = Number(form.dataset.minLength);
  let found = null;
  let timer = 0;
  let sequence = 0;

  const show = (text, isMatch) => {
    hint.textContent = text;
    hint.classList.toggle("text-success", isMatch);
  };

  const ask = async () => {
    const query = input.value.trim();
    found = null;
    if (query.length < minLength) {
      show(form.dataset.textShort, false);
      return;
    }

    const asked = ++sequence;
    const body = new URLSearchParams({ query });
    if (token) {
      body.set("__RequestVerificationToken", token.value);
    }

    try {
      const response = await fetch(form.dataset.findUrl, {
        method: "POST",
        body,
        credentials: "same-origin",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
      });
      const result = await response.json();
      if (asked !== sequence) {
        return;
      }

      if (result.status === "one") {
        found = result.name;
        show(form.dataset.textOne.replace("{0}", result.name), true);
      } else if (result.status === "expired") {
        show(form.dataset.textExpired, false);
      } else if (result.status === "ambiguous") {
        show(form.dataset.textAmbiguous, false);
      } else if (result.status === "none") {
        show(form.dataset.textNone, false);
      } else {
        show(form.dataset.textShort, false);
      }
    } catch {
      /* No hint is no harm: submitting still lets the server decide. */
    }
  };

  input.addEventListener("input", () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(ask, 200);
  });

  input.addEventListener("keydown", (event) => {
    const atEnd = input.selectionStart === input.value.length;
    if (found && found !== input.value && (event.key === "Tab" || (event.key === "ArrowRight" && atEnd))) {
      event.preventDefault();
      input.value = found;
    }
  });

  /* Pre-filled with the organisation chosen last time: confirm it at once, selected so typing replaces it. */
  if (input.value.trim()) {
    input.select();
    void ask();
  }
})();
