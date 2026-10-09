const state = {
  user: null,
  quizzes: [],
  activeQuizId: null,
  authMode: "login",
  socket: null,
  currentQuestionId: null
};

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];

document.addEventListener("DOMContentLoaded", async () => {
  bindEvents();
  await loadMe();
});

function bindEvents() {
  $$("[data-auth]").forEach((button) => button.addEventListener("click", () => setAuthMode(button.dataset.auth)));
  $$("[data-view]").forEach((button) => button.addEventListener("click", () => showView(button.dataset.view)));
  $("#authForm").addEventListener("submit", submitAuth);
  $("#logoutBtn").addEventListener("click", logout);
  $("#newQuizBtn").addEventListener("click", () => editQuiz(null));
  $("#addQuestionBtn").addEventListener("click", () => addQuestion());
  $("#saveQuizBtn").addEventListener("click", saveQuiz);
  $("#createSessionBtn").addEventListener("click", createSession);
  $("#joinRoomBtn").addEventListener("click", joinRoom);
  $("#startSessionBtn").addEventListener("click", () => sendRoom("start"));
  $("#nextQuestionBtn").addEventListener("click", () => sendRoom("next"));
  $("#endSessionBtn").addEventListener("click", () => sendRoom("end"));
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: { "Content-Type": "application/json", ...(options.headers || {}) },
    ...options
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({ message: "Ошибка запроса" }));
    throw new Error(error.message || error.Message || "Ошибка запроса");
  }
  return response.status === 204 ? null : response.json();
}

async function loadMe() {
  state.user = await api("/api/me");
  renderAuthState();
  if (state.user) {
    await loadDashboard();
    showView("dashboard");
  } else {
    $("#authView").classList.add("active");
    $$(".view").forEach((view) => view.classList.remove("active"));
  }
}

function renderAuthState() {
  const logged = Boolean(state.user);
  $("#authView").classList.toggle("active", !logged);
  $(".topbar").classList.toggle("hidden", !logged);
  $("#userBadge").textContent = logged ? `${state.user.displayName} · ${roleLabel(state.user.role)}` : "";
  $$(".organizer-only").forEach((el) => el.classList.toggle("hidden", state.user?.role !== "Organizer"));
}

function setAuthMode(mode) {
  state.authMode = mode;
  $$("[data-auth]").forEach((button) => button.classList.toggle("active", button.dataset.auth === mode));
  $$(".register-field").forEach((field) => field.classList.toggle("hidden", mode !== "register"));
}

async function submitAuth(event) {
  event.preventDefault();
  $("#authError").textContent = "";
  const form = new FormData(event.currentTarget);
  const payload = Object.fromEntries(form.entries());
  try {
    state.user = await api(`/api/${state.authMode}`, { method: "POST", body: JSON.stringify(payload) });
    renderAuthState();
    await loadDashboard();
    showView("dashboard");
  } catch (error) {
    $("#authError").textContent = error.message;
  }
}

async function logout() {
  await api("/api/logout", { method: "POST" });
  location.reload();
}

async function loadDashboard() {
  state.quizzes = await api("/api/quizzes");
  const history = await api("/api/history");
  renderQuizList();
  renderHistory(history);
  renderSessionQuizSelect();
}

function renderQuizList() {
  const list = $("#quizList");
  list.innerHTML = "";
  if (!state.quizzes.length) {
    list.innerHTML = `<p class="hint">Квизов пока нет.</p>`;
    return;
  }
  state.quizzes.forEach((quiz) => {
    const item = document.createElement("article");
    item.className = "list-item";
    item.innerHTML = `
      <strong>${escapeHtml(quiz.title)}</strong>
      <span class="meta">${escapeHtml(quiz.category)} · вопросов: ${quiz.questions.length} · ${quiz.secondsPerQuestion} сек.</span>
      <div class="actions">
        ${state.user.role === "Organizer" ? `<button data-edit="${quiz.id}">Редактировать</button><button data-run="${quiz.id}">Запустить</button>` : ""}
      </div>`;
    list.append(item);
  });
  $$("[data-edit]").forEach((button) => button.addEventListener("click", () => editQuiz(button.dataset.edit)));
  $$("[data-run]").forEach((button) => button.addEventListener("click", () => {
    $("#sessionQuiz").value = button.dataset.run;
    showView("room");
  }));
}

function renderHistory(items) {
  const list = $("#historyList");
  const values = Array.isArray(items) ? items : [...items];
  list.innerHTML = values.length ? "" : `<p class="hint">История появится после участия или запуска квиза.</p>`;
  values.forEach((item) => {
    const row = document.createElement("article");
    row.className = "list-item";
    row.innerHTML = `<strong>${escapeHtml(item.quiz || "Квиз")}</strong><span class="meta">Комната ${item.code} · ${item.status}${item.score !== undefined ? ` · ${item.score} баллов` : ""}</span>`;
    list.append(row);
  });
}

function renderSessionQuizSelect() {
  $("#sessionQuiz").innerHTML = state.quizzes.map((quiz) => `<option value="${quiz.id}">${escapeHtml(quiz.title)}</option>`).join("");
}

function showView(name) {
  $("#authView").classList.remove("active");
  $$(".view").forEach((view) => view.classList.toggle("active", view.id === `${name}View`));
}

function editQuiz(id) {
  const quiz = id ? state.quizzes.find((x) => x.id === id) : null;
  state.activeQuizId = quiz?.id || null;
  $("#quizTitle").value = quiz?.title || "";
  $("#quizCategory").value = quiz?.category || "";
  $("#quizSeconds").value = quiz?.secondsPerQuestion || 30;
  $("#quizRules").value = quiz?.rules || "";
  $("#questionsEditor").innerHTML = "";
  (quiz?.questions || []).forEach(addQuestion);
  if (!quiz) addQuestion();
  showView("builder");
}

function addQuestion(question = null) {
  const node = $("#questionTemplate").content.firstElementChild.cloneNode(true);
  node.dataset.id = question?.id || "";
  node.querySelector(".q-text").value = question?.text || "";
  node.querySelector(".q-kind").value = question?.kind || "Text";
  node.querySelector(".q-image").value = question?.imageUrl || "";
  node.querySelector(".q-mode").value = question?.choiceMode || "Single";
  node.querySelector(".add-option").addEventListener("click", () => addOption(node));
  node.querySelector(".remove-question").addEventListener("click", () => node.remove());
  $("#questionsEditor").append(node);
  (question?.options || [{ text: "", isCorrect: true }, { text: "", isCorrect: false }]).forEach((option) => addOption(node, option));
}

function addOption(questionNode, option = null) {
  const row = document.createElement("div");
  row.className = "option-row";
  row.dataset.id = option?.id || "";
  row.innerHTML = `
    <label>Вариант <input class="opt-text" value="${escapeAttr(option?.text || "")}"></label>
    <label><input class="opt-correct" type="checkbox" ${option?.isCorrect ? "checked" : ""}> Верный</label>
    <button class="ghost" type="button">Удалить</button>`;
  row.querySelector("button").addEventListener("click", () => row.remove());
  questionNode.querySelector(".options").append(row);
}

async function saveQuiz() {
  const questions = $$("#questionsEditor .question-card").map((card) => ({
    id: card.dataset.id,
    text: card.querySelector(".q-text").value,
    kind: card.querySelector(".q-kind").value,
    imageUrl: card.querySelector(".q-image").value,
    choiceMode: card.querySelector(".q-mode").value,
    options: [...card.querySelectorAll(".option-row")].map((row) => ({
      id: row.dataset.id,
      text: row.querySelector(".opt-text").value,
      isCorrect: row.querySelector(".opt-correct").checked
    }))
  }));
  const payload = {
    id: state.activeQuizId,
    title: $("#quizTitle").value,
    category: $("#quizCategory").value,
    secondsPerQuestion: Number($("#quizSeconds").value),
    rules: $("#quizRules").value,
    questions
  };
  await api("/api/quizzes", { method: "POST", body: JSON.stringify(payload) });
  await loadDashboard();
  showView("dashboard");
}

async function createSession() {
  const quizId = $("#sessionQuiz").value;
  if (!quizId) return;
  const session = await api("/api/sessions", { method: "POST", body: JSON.stringify({ quizId }) });
  $("#roomCode").value = session.code;
  await connectSocket(session.code);
}

async function joinRoom() {
  const code = $("#roomCode").value.trim().toUpperCase();
  if (!code) return;
  await api("/api/join", { method: "POST", body: JSON.stringify({ code }) });
  await connectSocket(code);
}

async function connectSocket(code) {
  if (state.socket) state.socket.close();
  const protocol = location.protocol === "https:" ? "wss" : "ws";
  state.socket = new WebSocket(`${protocol}://${location.host}/ws/quiz`);
  state.socket.addEventListener("open", () => {
    sendRoom("join-room", { code });
    $("#roomStatus").textContent = `Подключено к комнате ${code}.`;
  });
  state.socket.addEventListener("message", (event) => renderRoom(JSON.parse(event.data)));
  showView("room");
}

function sendRoom(type, extra = {}) {
  const code = extra.code || $("#roomCode").value.trim().toUpperCase();
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN || !code) return;
  state.socket.send(JSON.stringify({ type, code, ...extra }));
}

function renderRoom(message) {
  if (message.type !== "state") return;
  const { session, quiz, question, leaderboard } = message;
  $("#roomCode").value = session?.code || $("#roomCode").value;
  $("#roomStatus").textContent = quiz ? `${quiz.title} · статус: ${session.status} · вопрос ${Math.max(session.currentQuestionIndex + 1, 0)} из ${quiz.totalQuestions}` : "Ожидание квиза.";
  renderLeaderboard(leaderboard || []);
  renderQuestion(question, session?.status);
}

function renderLeaderboard(players) {
  $("#leaderboard").innerHTML = players.length
    ? players.map((p, index) => `<div class="leader"><span>${index + 1}. ${escapeHtml(p.displayName)}</span><strong>${p.score}</strong></div>`).join("")
    : `<p class="hint">Участники появятся после подключения.</p>`;
}

function renderQuestion(question, status) {
  const panel = $("#questionPanel");
  if (!question || status === "Lobby") {
    panel.innerHTML = `<h2>Ожидание старта</h2><p class="hint">Организатор запускает квиз, участники видят текущий вопрос синхронно.</p>`;
    return;
  }
  if (status === "Finished") {
    panel.innerHTML = `<h2>Квиз завершен</h2><p class="hint">Итоговый результат сохранен в истории.</p>`;
    loadDashboard();
    return;
  }

  state.currentQuestionId = question.id;
  const inputType = question.choiceMode === "Multiple" ? "checkbox" : "radio";
  panel.innerHTML = `
    <h2>${escapeHtml(question.text)}</h2>
    ${question.kind === "Image" && question.imageUrl ? `<img class="question-image" src="${escapeAttr(question.imageUrl)}" alt="Изображение вопроса">` : ""}
    <div class="answer-list">
      ${question.options.map((option) => `
        <label><input type="${inputType}" name="answer" value="${option.id}">${escapeHtml(option.text)}</label>
      `).join("")}
    </div>
    <button id="submitAnswerBtn">Ответить</button>`;
  $("#submitAnswerBtn").addEventListener("click", submitAnswer);
}

function submitAnswer() {
  const optionIds = $$("#questionPanel input[name='answer']:checked").map((input) => input.value);
  sendRoom("answer", { questionId: state.currentQuestionId, optionIds });
  $("#submitAnswerBtn").disabled = true;
  $("#submitAnswerBtn").textContent = "Ответ принят";
}

function roleLabel(role) {
  return role === "Organizer" ? "организатор" : "участник";
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#039;" }[char]));
}

function escapeAttr(value) {
  return escapeHtml(value).replace(/`/g, "&#096;");
}
