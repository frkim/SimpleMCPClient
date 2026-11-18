const state = {
  endpoint: "",
  bearerToken: "",
  tools: [],
  resources: [],
  prompts: [],
  activeView: "tools",
  page: 1,
  pageSize: 8,
  sortBy: "name",
  sortAscending: true,
  selected: null
};

const elements = Object.fromEntries([
  "connect-form", "endpoint", "token", "web-auth-token", "connect-button", "connection-state", "notice", "workspace",
  "disconnect-button", "search", "tools-body", "resources-list", "prompts-list", "result-summary",
  "page-label", "previous-page", "next-page", "action-panel", "action-title", "action-description",
  "arguments-input", "execute-button", "output-panel", "output-content", "tools-count", "resources-count",
  "prompts-count", "empty-tools", "empty-resources", "empty-prompts"
].map(id => [id, document.getElementById(id)]));

const themePreference = localStorage.getItem("theme");
if (themePreference === "dark" || (!themePreference && matchMedia("(prefers-color-scheme: dark)").matches)) {
  document.documentElement.dataset.theme = "dark";
}

document.getElementById("theme-toggle").addEventListener("click", () => {
  const dark = document.documentElement.dataset.theme !== "dark";
  document.documentElement.dataset.theme = dark ? "dark" : "light";
  localStorage.setItem("theme", dark ? "dark" : "light");
});

elements["connect-form"].addEventListener("submit", async event => {
  event.preventDefault();
  state.endpoint = elements.endpoint.value.trim();
  state.bearerToken = elements.token.value;
  setBusy(true);
  setNotice("Connecting and discovering server capabilities…");
  try {
    const [tools, resources, prompts] = await Promise.all([
      api("/api/tools"),
      api("/api/resources"),
      api("/api/prompts")
    ]);
    state.tools = tools;
    state.resources = resources;
    state.prompts = prompts;
    elements["connection-state"].textContent = "CONNECTED";
    elements["connection-state"].classList.add("connected");
    elements.workspace.classList.remove("hidden");
    elements["tools-count"].textContent = tools.length;
    elements["resources-count"].textContent = resources.length;
    elements["prompts-count"].textContent = prompts.length;
    setNotice(`${tools.length} tools, ${resources.length} resources, and ${prompts.length} prompts discovered.`);
    render();
    elements.workspace.scrollIntoView({ behavior: "smooth", block: "start" });
  } catch (error) {
    setNotice(error.message, true);
  } finally {
    setBusy(false);
  }
});

document.getElementById("disconnect-button").addEventListener("click", () => {
  state.endpoint = "";
  state.bearerToken = "";
  state.tools = [];
  state.resources = [];
  state.prompts = [];
  state.selected = null;
  elements.endpoint.value = "";
  elements.token.value = "";
  elements["web-auth-token"].value = "";
  elements["connection-state"].textContent = "NOT CONNECTED";
  elements["connection-state"].classList.remove("connected");
  elements.workspace.classList.add("hidden");
  elements["action-panel"].classList.add("hidden");
  setNotice("Disconnected. Credentials have been cleared.");
});

document.querySelectorAll(".tab").forEach(tab => tab.addEventListener("click", () => {
  state.activeView = tab.dataset.view;
  state.page = 1;
  document.querySelectorAll(".tab").forEach(item => {
    const active = item === tab;
    item.classList.toggle("active", active);
    item.setAttribute("aria-selected", String(active));
  });
  document.querySelectorAll(".view-panel").forEach(panel => panel.classList.add("hidden"));
  document.getElementById(`${state.activeView}-view`).classList.remove("hidden");
  render();
}));

elements.search.addEventListener("input", () => {
  state.page = 1;
  render();
});

document.querySelectorAll(".sort-button").forEach(button => button.addEventListener("click", () => {
  if (state.sortBy === button.dataset.sort) {
    state.sortAscending = !state.sortAscending;
  } else {
    state.sortBy = button.dataset.sort;
    state.sortAscending = true;
  }
  render();
}));

elements["previous-page"].addEventListener("click", () => {
  state.page = Math.max(1, state.page - 1);
  render();
});
elements["next-page"].addEventListener("click", () => {
  state.page++;
  render();
});
elements["execute-button"].addEventListener("click", runSelected);

async function api(path, body = {}) {
  const response = await fetch(path, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(elements["web-auth-token"].value
        ? { Authorization: "Bearer " + elements["web-auth-token"].value }
        : {})
    },
    body: JSON.stringify({
      endpoint: state.endpoint,
      bearerToken: state.bearerToken || null,
      name: body.name ?? null,
      arguments: body.arguments ?? null,
      uri: body.uri ?? null
    })
  });
  const payload = await response.json();
  if (!response.ok) {
    const suffix = payload.correlationId ? ` (Reference: ${payload.correlationId})` : "";
    throw new Error(`${payload.error || "The request failed."}${suffix}`);
  }
  return payload;
}

function render() {
  const collection = state[state.activeView];
  const query = elements.search.value.trim().toLocaleLowerCase();
  const filtered = collection.filter(item => JSON.stringify(item).toLocaleLowerCase().includes(query));
  const sorted = state.activeView === "tools"
    ? [...filtered].sort((a, b) => String(a[state.sortBy] || "").localeCompare(String(b[state.sortBy] || "")) * (state.sortAscending ? 1 : -1))
    : filtered;
  const pageCount = Math.max(1, Math.ceil(sorted.length / state.pageSize));
  state.page = Math.min(state.page, pageCount);
  const pageItems = sorted.slice((state.page - 1) * state.pageSize, state.page * state.pageSize);
  elements["result-summary"].textContent = `${filtered.length} ${state.activeView}`;
  elements["page-label"].textContent = `Page ${state.page} of ${pageCount}`;
  elements["previous-page"].disabled = state.page <= 1;
  elements["next-page"].disabled = state.page >= pageCount;
  elements["empty-tools"].classList.toggle("hidden", state.activeView !== "tools" || pageItems.length !== 0);
  elements["empty-resources"].classList.toggle("hidden", state.activeView !== "resources" || pageItems.length !== 0);
  elements["empty-prompts"].classList.toggle("hidden", state.activeView !== "prompts" || pageItems.length !== 0);

  if (state.activeView === "tools") {
    renderTools(pageItems);
  } else if (state.activeView === "resources") {
    renderItems(pageItems, elements["resources-list"], "▤", "resource");
  } else {
    renderItems(pageItems, elements["prompts-list"], "✳", "prompt");
  }
}

function renderTools(tools) {
  elements["tools-body"].replaceChildren();
  for (const tool of tools) {
    const row = document.createElement("tr");
    const nameCell = document.createElement("td");
    const name = document.createElement("span");
    name.className = "tool-name";
    name.textContent = tool.name || "Unnamed tool";
    nameCell.append(name);

    const descriptionCell = document.createElement("td");
    descriptionCell.className = "tool-description";
    descriptionCell.textContent = tool.description || "No description provided.";

    const actionCell = document.createElement("td");
    const button = document.createElement("button");
    button.className = "small-button";
    button.type = "button";
    button.textContent = "Use tool";
    button.addEventListener("click", () => selectCapability("tool", tool));
    actionCell.append(button);
    row.append(nameCell, descriptionCell, actionCell);
    elements["tools-body"].append(row);
  }
}

function renderItems(items, container, symbol, kind) {
  container.replaceChildren();
  for (const item of items) {
    const card = document.createElement("article");
    card.className = "item-card";
    const icon = document.createElement("span");
    icon.className = "item-symbol";
    icon.textContent = symbol;
    const copy = document.createElement("div");
    copy.className = "item-copy";
    const title = document.createElement("strong");
    title.textContent = item.name || item.uri || "Unnamed";
    const description = document.createElement("p");
    description.textContent = item.description || item.uri || "No description provided.";
    copy.append(title, description);
    const button = document.createElement("button");
    button.className = "small-button";
    button.type = "button";
    button.textContent = kind === "resource" ? "Read" : "Use prompt";
    button.addEventListener("click", () => selectCapability(kind, item));
    card.append(icon, copy, button);
    container.append(card);
  }
}

function selectCapability(kind, item) {
  state.selected = { kind, item };
  elements["action-title"].textContent = kind === "tool" ? `Run ${item.name}` : kind === "resource" ? `Read ${item.name || item.uri}` : `Get ${item.name}`;
  elements["action-description"].textContent = item.description || "Provide any required JSON arguments below.";
  elements["arguments-input"].value = "{}";
  elements["arguments-input"].disabled = kind === "resource";
  elements["execute-button"].textContent = kind === "resource" ? "Read resource →" : kind === "prompt" ? "Get prompt →" : "Run →";
  elements["output-panel"].classList.add("hidden");
  elements["action-panel"].classList.remove("hidden");
  elements["action-panel"].scrollIntoView({ behavior: "smooth", block: "nearest" });
}

async function runSelected() {
  if (!state.selected) return;
  let args;
  try {
    args = JSON.parse(elements["arguments-input"].value || "{}");
    if (args === null || Array.isArray(args) || typeof args !== "object") {
      throw new Error("Arguments must be a JSON object.");
    }
  } catch (error) {
    showOutput(error.message);
    return;
  }

  elements["execute-button"].disabled = true;
  elements["execute-button"].textContent = "Working…";
  try {
    const { kind, item } = state.selected;
    const result = kind === "tool"
      ? await api("/api/tools/call", { name: item.name, arguments: args })
      : kind === "resource"
        ? await api("/api/resources/read", { uri: item.uri })
        : await api("/api/prompts/get", { name: item.name, arguments: args });
    showOutput(JSON.stringify(result, null, 2));
  } catch (error) {
    showOutput(error.message);
  } finally {
    elements["execute-button"].disabled = false;
    elements["execute-button"].textContent = state.selected.kind === "resource" ? "Read resource →" : state.selected.kind === "prompt" ? "Get prompt →" : "Run →";
  }
}

function showOutput(value) {
  elements["output-content"].textContent = value;
  elements["output-panel"].classList.remove("hidden");
}

function setBusy(busy) {
  elements["connect-button"].disabled = busy;
  elements["connect-button"].firstElementChild.textContent = busy ? "Connecting…" : "Connect";
}

function setNotice(text, error = false) {
  elements.notice.textContent = text;
  elements.notice.classList.toggle("error", error);
}
