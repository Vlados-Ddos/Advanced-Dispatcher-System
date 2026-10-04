// This small, dependency-free script must run before the application module.
// It remains usable when an import fails or application initialization stalls.
(() => {
  const screen = document.getElementById("boot-screen");
  const message = document.getElementById("boot-message");
  const reload = document.getElementById("boot-reload");
  const ru = navigator.language?.startsWith("ru");
  // Audited separately: this fallback must work when localization.js cannot load.
  const bootMessages = {
    loading: ["Loading the web interface… If loading does not finish, reload the page and check that JavaScript is enabled in your browser.", "Загрузка веб-интерфейса… Если загрузка не завершится, перезагрузите страницу и проверьте, включён ли JavaScript в браузере."],
    failed: ["The web interface could not start. Reload the page. If this happens again, check the Web Host and update its web interface files together.", "Не удалось запустить веб-интерфейс. Перезагрузите страницу. Если ошибка повторится, проверьте Web Host и обновите файлы веб-интерфейса вместе с ним."],
    reload: ["Reload page", "Перезагрузить страницу"],
  };
  let ready = false;
  const fail = () => {
    if (ready) return;
    clearTimeout(timer);
    message.textContent = bootMessages.failed[ru ? 1 : 0];
    screen.hidden = false;
  };
  const timer = setTimeout(fail, 18000);
  message.textContent = bootMessages.loading[ru ? 1 : 0];
  reload.textContent = bootMessages.reload[ru ? 1 : 0];
  window.addEventListener("error", fail, true);
  window.addEventListener("unhandledrejection", fail);
  window.addEventListener("ads-app-ready", () => {
    ready = true;
    clearTimeout(timer);
    screen.hidden = true;
    window.removeEventListener("error", fail, true);
    window.removeEventListener("unhandledrejection", fail);
  }, { once: true });
})();
