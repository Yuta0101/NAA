export type ChatRole = "user" | "assistant";

export interface ChatMessage {
  id: string;
  role: ChatRole;
  text: string;
  createdAt: Date;
  tone?: "default" | "error" | "success";
}

export interface HistoryTurn {
  uniqueId: string;
  conversationId: string;
  questionText: string;
  answerText: string;
  createdAt?: Date;
  isDeleted: boolean;
}

export interface ConversationSummary {
  conversationId: string;
  account?: string;
  chatTitle: string;
  lastQuestionText: string;
  lastAnswerText: string;
  originCode?: string;
  lastMessageAt?: Date;
  turnCount: number;
  isDeleted: boolean;
  deletedAt?: Date;
  deletedBy?: string;
}

export interface BoundCard {
  cardId: string;
  maskedUid: string;
  boundAt?: Date;
}

export interface ReviseRequest {
  conversationId: string;
  chatTitle: string;
  inputText: string;
  account: string;
  employeeId: string;
  originCode: "DDM";
}

export interface ReviseResult {
  answer: string | null;
  status: number;
}
