import {
  AssistantRuntimeProvider,
  type AppendMessage,
  type ThreadMessageLike,
  useExternalStoreRuntime,
} from "@assistant-ui/react";
import {
  ArrowLeft,
  BookOpenText,
  ChevronUp,
  CircleUserRound,
  LogOut,
  Menu,
  MessageSquarePlus,
  PanelLeftClose,
  PanelLeftOpen,
  RefreshCw,
  RotateCcw,
  Settings,
  Trash2,
  X,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  getConversationById,
  getConversationSummaries,
  restoreConversation,
  reviseText,
  softDeleteConversation,
} from "../api/ddm";
import type { ChatMessage, ConversationSummary, HistoryTurn } from "../types";
import {
  AnimatedThemeToggler,
  type AppTheme,
} from "./AnimatedThemeToggler";
import { AssistantThread } from "./AssistantThread";
import { Brand } from "./Brand";
import { CardSettings } from "./CardSettings";
import { NaaIcon } from "./NaaIcon";

interface ChatPageProps {
  account: string;
  theme: AppTheme;
  onThemeChange: (theme: AppTheme) => void;
  onLogout: () => void;
}

function createId(prefix: string): string {
  if (typeof crypto.randomUUID === "function") return `${prefix}-${crypto.randomUUID()}`;
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function createConversationId(): string {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 20)}`.slice(0, 36);
}

function readAppendText(message: AppendMessage): string {
  return message.content
    .filter((part): part is Extract<(typeof message.content)[number], { type: "text" }> => part.type === "text")
    .map((part) => part.text)
    .join("\n")
    .trim();
}

function historyMessages(turns: HistoryTurn[]): ChatMessage[] {
  const result: ChatMessage[] = [];
  turns.forEach((turn, index) => {
    if (turn.questionText.trim()) {
      result.push({
        id: `${turn.uniqueId || `${turn.conversationId}-${index}`}-q`,
        role: "user",
        text: turn.questionText,
        createdAt: turn.createdAt || new Date(),
      });
    }
    if (turn.answerText.trim()) {
      result.push({
        id: `${turn.uniqueId || `${turn.conversationId}-${index}`}-a`,
        role: "assistant",
        text: turn.answerText,
        createdAt: turn.createdAt || new Date(),
        tone: turn.answerText.startsWith("系統暫時無法取得回答") ? "error" : "default",
      });
    }
  });
  return result;
}

function formatHistoryTime(date?: Date): string {
  if (!date) return "歷史紀錄";
  const today = new Date();
  if (date.toDateString() === today.toDateString()) {
    return date.toLocaleTimeString("zh-TW", { hour: "2-digit", minute: "2-digit" });
  }
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);
  if (date.toDateString() === yesterday.toDateString()) return "昨天";
  return date.toLocaleDateString("zh-TW", { month: "2-digit", day: "2-digit" });
}

export function ChatPage({ account, theme, onThemeChange, onLogout }: ChatPageProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [history, setHistory] = useState<ConversationSummary[]>([]);
  const [deletedHistory, setDeletedHistory] = useState<ConversationSummary[]>([]);
  const [showDeleted, setShowDeleted] = useState(false);
  const [selectedConversationId, setSelectedConversationId] = useState<string>();
  const [selectedConversationIsDeleted, setSelectedConversationIsDeleted] = useState(false);
  const [conversationId, setConversationId] = useState(createConversationId);
  const [isRunning, setIsRunning] = useState(false);
  const [isHistoryLoading, setIsHistoryLoading] = useState(true);
  const [historyError, setHistoryError] = useState("");
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const [accountMenuOpen, setAccountMenuOpen] = useState(false);
  const [accountPanel, setAccountPanel] = useState<"menu" | "settings" | "help">("menu");
  const accountMenuRef = useRef<HTMLDivElement>(null);

  const closeAccountMenu = useCallback(() => {
    setAccountMenuOpen(false);
    setAccountPanel("menu");
  }, []);

  const refreshHistory = useCallback(async () => {
    setIsHistoryLoading(true);
    setHistoryError("");
    try {
      const [active, deleted] = await Promise.all([
        getConversationSummaries(account, false),
        getConversationSummaries(account, true),
      ]);
      setHistory(active);
      setDeletedHistory(deleted);
    } catch (error) {
      setHistoryError(error instanceof Error ? error.message : "暫時無法載入歷史紀錄。");
    } finally {
      setIsHistoryLoading(false);
    }
  }, [account]);

  useEffect(() => {
    void refreshHistory();
  }, [refreshHistory]);

  useEffect(() => {
    if (!accountMenuOpen) return;

    const closeOnOutsideClick = (event: PointerEvent) => {
      if (!accountMenuRef.current?.contains(event.target as Node)) closeAccountMenu();
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") closeAccountMenu();
    };

    document.addEventListener("pointerdown", closeOnOutsideClick);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsideClick);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [accountMenuOpen, closeAccountMenu]);

  const startNewChat = useCallback(() => {
    setMessages([]);
    setSelectedConversationId(undefined);
    setSelectedConversationIsDeleted(false);
    setConversationId(createConversationId());
    setSidebarOpen(false);
    closeAccountMenu();
  }, [closeAccountMenu]);

  const openHistory = useCallback(async (item: ConversationSummary) => {
    setHistoryError("");
    try {
      const turns = await getConversationById(account, item.conversationId, item.isDeleted);
      setMessages(historyMessages(turns));
      setSelectedConversationId(item.conversationId);
      setSelectedConversationIsDeleted(item.isDeleted);
      setConversationId(item.conversationId);
      setSidebarOpen(false);
      closeAccountMenu();
    } catch (error) {
      setHistoryError(error instanceof Error ? error.message : "無法載入完整對話。");
    }
  }, [account, closeAccountMenu]);

  const changeDeletedState = useCallback(async (item: ConversationSummary) => {
    setHistoryError("");
    try {
      if (item.isDeleted) {
        await restoreConversation(account, item.conversationId);
      } else {
        await softDeleteConversation(account, item.conversationId);
        if (selectedConversationId === item.conversationId) startNewChat();
      }
      await refreshHistory();
    } catch (error) {
      setHistoryError(error instanceof Error ? error.message : "無法更新對話狀態。");
    }
  }, [account, refreshHistory, selectedConversationId, startNewChat]);

  const onNew = useCallback(
    async (incoming: AppendMessage) => {
      const text = readAppendText(incoming);
      if (!text || isRunning) return;

      if (selectedConversationIsDeleted) {
        setMessages((current) => [
          ...current,
          {
            id: createId("error"),
            role: "assistant",
            text: "這個對話已刪除，請先從「已刪除」列表恢復後再繼續。",
            createdAt: new Date(),
            tone: "error",
          },
        ]);
        return;
      }

      const userMessage: ChatMessage = {
        id: createId("user"),
        role: "user",
        text,
        createdAt: new Date(),
      };

      setMessages((current) => [...current, userMessage]);
      setSelectedConversationId(conversationId);
      setIsRunning(true);

      try {
        const response = await reviseText({
          conversationId,
          chatTitle: text,
          inputText: text,
          account,
          employeeId: account,
          originCode: "DDM",
        });

        setMessages((current) => [
          ...current,
          {
            id: createId("assistant"),
            role: "assistant",
            text: response.answer || "目前知識庫無資料",
            createdAt: new Date(),
            tone: "default",
          },
        ]);
        setIsRunning(false);
        await refreshHistory();
      } catch (error) {
        setMessages((current) => [
          ...current,
          {
            id: createId("error"),
            role: "assistant",
            text: error instanceof Error ? error.message : "DDM 請求失敗，請稍後再試。",
            createdAt: new Date(),
            tone: "error",
          },
        ]);
        setIsRunning(false);
        await refreshHistory();
      } finally {
        setIsRunning(false);
      }
    },
    [account, conversationId, isRunning, refreshHistory, selectedConversationIsDeleted],
  );

  const convertMessage = useCallback(
    (message: ChatMessage): ThreadMessageLike => ({
      id: message.id,
      role: message.role,
      content: [{ type: "text", text: message.text }],
      createdAt: message.createdAt,
      status: message.role === "assistant" ? { type: "complete", reason: "stop" } : undefined,
      metadata: { custom: { tone: message.tone || "default" } },
    }),
    [],
  );

  const runtime = useExternalStoreRuntime({
    messages,
    isRunning,
    onNew,
    convertMessage,
  });

  const initial = useMemo(() => account.trim().charAt(0).toUpperCase() || "護", [account]);
  const displayedHistory = showDeleted ? deletedHistory : history;

  return (
    <AssistantRuntimeProvider runtime={runtime}>
      <div className={`app-shell ${sidebarCollapsed ? "app-shell--sidebar-collapsed" : ""}`}>
        <aside
          className={`sidebar ${sidebarOpen ? "sidebar--open" : ""} ${
            sidebarCollapsed ? "sidebar--collapsed" : ""
          }`}
        >
          <div className="sidebar__desktop-head">
            <Brand className="sidebar__brand" />
            <button
              type="button"
              className="sidebar__toggle micro-button micro-button--icon-shift"
              onClick={() => setSidebarCollapsed((collapsed) => !collapsed)}
              aria-label={sidebarCollapsed ? "展開側欄" : "收合側欄"}
              title={sidebarCollapsed ? "展開側欄" : "收合側欄"}
            >
              {sidebarCollapsed ? <PanelLeftOpen size={20} /> : <PanelLeftClose size={20} />}
            </button>
          </div>

          <div className="sidebar__mobile-head">
            <Brand />
            <button className="micro-button" type="button" onClick={() => setSidebarOpen(false)} aria-label="關閉側欄">
              <X size={20} />
            </button>
          </div>

          <button type="button" className="new-chat-button micro-button micro-button--glare" onClick={startNewChat}>
            <MessageSquarePlus size={19} />
            <span>開始新對話</span>
          </button>

          <div className="sidebar__history">
            <div className="history-heading">
              <span>{showDeleted ? "已刪除對話" : "最近對話"}</span>
              <div className="history-heading__actions">
                <button
                  type="button"
                  className="history-heading__toggle micro-button"
                  onClick={() => setShowDeleted((current) => !current)}
                  aria-label={showDeleted ? "顯示最近對話" : "顯示已刪除對話"}
                >
                  {showDeleted ? "最近" : "已刪除"}
                </button>
                <button
                  type="button"
                  className="micro-button micro-button--rotate"
                  onClick={() => void refreshHistory()}
                  disabled={isHistoryLoading}
                  aria-label="重新整理歷史紀錄"
                >
                  <RefreshCw className={isHistoryLoading ? "is-spinning" : ""} size={15} />
                </button>
              </div>
            </div>

            <div className="history-panel">
              {isHistoryLoading ? (
                <div className="history-state">
                  <span className="history-state__spinner" />
                  <span>載入歷史紀錄…</span>
                </div>
              ) : historyError ? (
                <div className="history-state history-state--error">
                  <strong>暫時無法載入</strong>
                  <span>{historyError}</span>
                </div>
              ) : displayedHistory.length === 0 ? (
                <div className="history-state">
                  <NaaIcon size={30} />
                  <strong>{showDeleted ? "沒有已刪除對話" : "尚無對話紀錄"}</strong>
                  <span>{showDeleted ? "刪除的對話會顯示在這裡。" : "完成一組問答後，紀錄會顯示在這裡。"}</span>
                </div>
              ) : (
                <div className="history-list">
                  {displayedHistory.slice(0, 20).map((item) => (
                    <div className="history-list__item" key={item.conversationId}>
                      <button
                        type="button"
                        className={`history-list__open micro-button ${selectedConversationId === item.conversationId ? "is-selected" : ""}`}
                        onClick={() => void openHistory(item)}
                        title={item.lastQuestionText}
                      >
                        <strong>{item.chatTitle}</strong>
                        <span>{item.lastQuestionText || "已保存的對話"}</span>
                        <small>{formatHistoryTime(item.lastMessageAt)}</small>
                      </button>
                      <button
                        type="button"
                        className="history-list__action micro-button"
                        onClick={() => void changeDeletedState(item)}
                        aria-label={item.isDeleted ? "恢復對話" : "刪除對話"}
                        title={item.isDeleted ? "恢復對話" : "移到已刪除"}
                      >
                        {item.isDeleted ? <RotateCcw size={15} /> : <Trash2 size={15} />}
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          <div className="sidebar-account" ref={accountMenuRef}>
            {accountMenuOpen && (
              <div className="sidebar-account__popover" role="dialog" aria-label="帳號選單">
                {accountPanel === "menu" ? (
                  <>
                    <div className="sidebar-account__summary">
                      <span className="sidebar-account__avatar">{initial}</span>
                      <span>
                        <strong>{account}</strong>
                        <small>護理同仁</small>
                      </span>
                    </div>
                    <div className="sidebar-account__divider" />
                    <button className="micro-button micro-button--icon-shift" type="button" onClick={() => setAccountPanel("settings")}>
                      <Settings size={18} />
                      <span>個人設定</span>
                    </button>
                    <button className="micro-button micro-button--icon-shift" type="button" onClick={() => setAccountPanel("help")}>
                      <BookOpenText size={18} />
                      <span>使用說明</span>
                    </button>
                    <AnimatedThemeToggler theme={theme} onThemeChange={onThemeChange} />
                    <div className="sidebar-account__divider" />
                    <button type="button" className="sidebar-account__logout micro-button micro-button--icon-shift" onClick={onLogout}>
                      <LogOut size={18} />
                      <span>登出</span>
                    </button>
                  </>
                ) : (
                  <>
                    <div className="sidebar-account__panel-head">
                      <button className="micro-button" type="button" onClick={() => setAccountPanel("menu")} aria-label="返回帳號選單">
                        <ArrowLeft size={18} />
                      </button>
                      <strong>{accountPanel === "settings" ? "個人設定" : "使用說明"}</strong>
                    </div>
                    {accountPanel === "settings" ? (
                      <>
                        <div className="sidebar-account__details">
                          <CircleUserRound size={26} />
                          <dl>
                            <div><dt>員工帳號</dt><dd>{account}</dd></div>
                            <div><dt>使用身分</dt><dd>護理同仁</dd></div>
                          </dl>
                        </div>
                        <CardSettings />
                      </>
                    ) : (
                      <ol className="sidebar-account__help">
                        <li>點擊「開始新對話」建立新聊天。</li>
                        <li>從最近對話開啟過往問答紀錄。</li>
                        <li>在下方輸入問題並送出給小護天使。</li>
                        <li>在「個人設定」綁定員工卡後，登入頁可直接刷卡登入。</li>
                      </ol>
                    )}
                  </>
                )}
              </div>
            )}
            <button
              type="button"
              className="sidebar-account__trigger micro-button"
              onClick={() => {
                if (!accountMenuOpen) setAccountPanel("menu");
                setAccountMenuOpen((open) => !open);
              }}
              aria-expanded={accountMenuOpen}
              aria-haspopup="dialog"
              title={sidebarCollapsed ? account : undefined}
            >
              <span className="sidebar-account__avatar">{initial}</span>
              <span className="sidebar-account__copy">
                <strong>{account}</strong>
                <small>護理同仁</small>
              </span>
              <ChevronUp className="sidebar-account__chevron" size={16} />
            </button>
          </div>
        </aside>

        {sidebarOpen && (
          <button
            type="button"
            className="sidebar-backdrop"
            onClick={() => setSidebarOpen(false)}
            aria-label="關閉側欄"
          />
        )}

        <main className="workspace">
          <button
            type="button"
            className="workspace__sidebar-open micro-button micro-button--icon-shift"
            onClick={() => setSidebarOpen(true)}
            aria-label="開啟對話選單"
          >
            <Menu size={21} />
          </button>

          <section className="workspace__content">
            <AssistantThread account={account} />
          </section>
        </main>
      </div>
    </AssistantRuntimeProvider>
  );
}
