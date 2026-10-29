import type {
  BoundCard,
  ConversationSummary,
  HistoryTurn,
  ReviseRequest,
  ReviseResult,
} from "../types";

const API_BASE = (import.meta.env.VITE_DDM_BASE_URL || "/ddm").replace(/\/$/, "");
// One blocking AgentBuilder request may take up to two minutes on the DDM.
const REQUEST_TIMEOUT_MS = 150_000;

type JsonObject = Record<string, unknown>;

function isObject(value: unknown): value is JsonObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readValue(source: JsonObject, names: string[]): unknown {
  const wanted = new Set(names.map((name) => name.toLowerCase()));
  const entry = Object.entries(source).find(([key]) => wanted.has(key.toLowerCase()));
  return entry?.[1];
}

function readText(source: JsonObject, ...names: string[]): string | undefined {
  const value = readValue(source, names);
  if (typeof value === "string") return value;
  if (typeof value === "number") return String(value);
  return undefined;
}

function readBoolean(source: JsonObject, ...names: string[]): boolean {
  const value = readValue(source, names);
  return value === true || value === 1 || value === "true";
}

function readNumber(source: JsonObject, ...names: string[]): number {
  const value = readValue(source, names);
  return typeof value === "number" ? value : Number(value) || 0;
}

function readDate(source: JsonObject, ...names: string[]): Date | undefined {
  const value = readText(source, ...names);
  if (!value) return undefined;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed;
}

function readProblemDetail(body: unknown): string | undefined {
  if (typeof body === "string") return body.trim() || undefined;
  if (!isObject(body)) return undefined;
  return readText(body, "detail", "description", "message", "title");
}

async function readResponseBody(response: Response): Promise<unknown> {
  const raw = await response.text();
  if (!raw) return undefined;

  try {
    return JSON.parse(raw) as unknown;
  } catch {
    return raw;
  }
}

export async function authenticateUser(account: string, password: string): Promise<string> {
  const response = await fetch("/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ account, password }),
  });
  const body = await readResponseBody(response);

  if (!response.ok) {
    throw new Error(readProblemDetail(body) || "帳號或密碼錯誤。");
  }

  if (!isObject(body)) throw new Error("登入服務回傳格式錯誤。");
  const authenticatedAccount = readText(body, "account")?.trim();
  if (!authenticatedAccount) throw new Error("登入服務沒有回傳帳號。");
  return authenticatedAccount;
}

export async function authenticateCard(cardUid: string): Promise<string> {
  let response: Response;
  try {
    response = await fetch("/auth/card-login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ cardUid }),
    });
  } catch {
    throw new Error("無法連線到登入服務，請確認 Front 已啟動。");
  }
  const body = await readResponseBody(response);

  if (!response.ok) {
    throw new Error(readProblemDetail(body) || "此卡片尚未綁定帳號。");
  }

  if (!isObject(body)) throw new Error("登入服務回傳格式錯誤。");
  const authenticatedAccount = readText(body, "account")?.trim();
  if (!authenticatedAccount) throw new Error("登入服務沒有回傳帳號。");
  return authenticatedAccount;
}

export async function getSessionAccount(): Promise<string | null> {
  const response = await fetch("/auth/session", { cache: "no-store" });
  if (response.status === 401) return null;

  const body = await readResponseBody(response);
  if (!response.ok || !isObject(body)) return null;
  return readText(body, "account")?.trim() || null;
}

export async function logoutUser(): Promise<void> {
  await fetch("/auth/logout", { method: "POST" });
}

async function postJson(path: string, payload: unknown): Promise<{ body: unknown; status: number }> {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

  try {
    const response = await fetch(`${API_BASE}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
      signal: controller.signal,
    });
    const body = await readResponseBody(response);

    if (!response.ok) {
      const detail = readProblemDetail(body);
      throw new Error(`DDM 回應 HTTP ${response.status}${detail ? `：${detail}` : ""}`);
    }

    return { body, status: response.status };
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw new Error("DDM 回應逾時，請確認服務是否正常執行。");
    }
    if (error instanceof TypeError) {
      throw new Error("無法連線到 DDM，請確認 7079 連接埠是否正常執行。");
    }
    throw error;
  } finally {
    window.clearTimeout(timeout);
  }
}

function envelopeResults(body: unknown): unknown[] {
  if (!isObject(body)) return [];
  const results = readValue(body, ["results"]);
  return Array.isArray(results) ? results : [];
}

function envelopeError(body: unknown): string | undefined {
  if (!isObject(body)) return undefined;
  const status = readNumber(body, "status");
  if (status === 1) return undefined;
  return readText(body, "description", "message") || "操作失敗";
}

function buildTitle(question: string): string {
  const value = question.trim() || "新的對話";
  return value.length <= 24 ? value : `${value.slice(0, 24)}…`;
}

function mapSummary(value: unknown): ConversationSummary | undefined {
  if (!isObject(value)) return undefined;
  const conversationId = readText(value, "conversationId");
  if (!conversationId) return undefined;
  const lastQuestionText = readText(value, "lastQuestionText") || "";

  return {
    conversationId,
    account: readText(value, "account"),
    chatTitle: readText(value, "chatTitle") || buildTitle(lastQuestionText),
    lastQuestionText,
    lastAnswerText: readText(value, "lastAnswerText") || "",
    originCode: readText(value, "originCode"),
    lastMessageAt: readDate(value, "lastMessageAt"),
    turnCount: readNumber(value, "turnCount"),
    isDeleted: readBoolean(value, "isDeleted"),
    deletedAt: readDate(value, "deletedAt"),
    deletedBy: readText(value, "deletedBy"),
  };
}

function mapTurn(value: unknown): HistoryTurn | undefined {
  if (!isObject(value)) return undefined;
  const uniqueId = readText(value, "uniqueId");
  const conversationId = readText(value, "conversationId");
  if (!uniqueId || !conversationId) return undefined;

  return {
    uniqueId,
    conversationId,
    questionText: readText(value, "questionText") || "",
    answerText: readText(value, "answerText") || "",
    createdAt: readDate(value, "insertDt", "createdAt"),
    isDeleted: readBoolean(value, "isDeleted"),
  };
}

export async function reviseText(request: ReviseRequest): Promise<ReviseResult> {
  const { body, status } = await postJson("/ReviseText", request);
  const answer = isObject(body) ? readText(body, "revisedText")?.trim() || null : null;
  return { answer, status };
}

export async function getConversationSummaries(
  account: string,
  isDeleted: boolean,
): Promise<ConversationSummary[]> {
  const { body } = await postJson("/GetConversationSummaries", {
    account,
    originCode: "DDM",
    isDeleted,
  });

  return envelopeResults(body)
    .map(mapSummary)
    .filter((item): item is ConversationSummary => item !== undefined);
}

export async function getConversationById(
  account: string,
  conversationId: string,
  includeDeleted: boolean,
): Promise<HistoryTurn[]> {
  const { body } = await postJson("/GetConversationById", {
    account,
    conversationId,
    originCode: "DDM",
    includeDeleted,
  });

  const turns = envelopeResults(body)
    .map(mapTurn)
    .filter((item): item is HistoryTurn => item !== undefined);

  if (turns.length === 0) throw new Error(envelopeError(body) || "找不到這個對話。");
  return turns;
}

async function mutateConversation(
  path: "/SoftDeleteConversation" | "/RestoreConversation",
  account: string,
  conversationId: string,
): Promise<void> {
  const { body } = await postJson(path, { account, conversationId });
  const error = envelopeError(body);
  if (error) throw new Error(error);
}

export function softDeleteConversation(account: string, conversationId: string): Promise<void> {
  return mutateConversation("/SoftDeleteConversation", account, conversationId);
}

export function restoreConversation(account: string, conversationId: string): Promise<void> {
  return mutateConversation("/RestoreConversation", account, conversationId);
}

function mapCard(value: unknown): BoundCard | undefined {
  if (!isObject(value)) return undefined;
  const cardId = readText(value, "cardId");
  if (!cardId) return undefined;
  return {
    cardId,
    maskedUid: readText(value, "maskedUid") || "",
    boundAt: readDate(value, "boundAt"),
  };
}

export async function getCards(): Promise<BoundCard[]> {
  // The Front proxy replaces account with the signed-in account.
  const { body } = await postJson("/GetCards", {});
  return envelopeResults(body)
    .map(mapCard)
    .filter((item): item is BoundCard => item !== undefined);
}

export async function bindCard(cardUid: string): Promise<string> {
  const { body } = await postJson("/BindCard", { cardUid });
  const error = envelopeError(body);
  if (error) throw new Error(error);
  return (isObject(body) && readText(body, "description")) || "卡片綁定成功";
}

export async function unbindCard(cardId: string): Promise<void> {
  const { body } = await postJson("/UnbindCard", { cardId });
  const error = envelopeError(body);
  if (error) throw new Error(error);
}
