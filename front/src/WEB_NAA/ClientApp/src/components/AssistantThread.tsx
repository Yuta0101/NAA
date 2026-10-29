import {
  AuiIf,
  ComposerPrimitive,
  MessagePrimitive,
  ThreadPrimitive,
  useAui,
} from "@assistant-ui/react";
import {
  ArrowDown,
  ArrowUp,
  Check,
  Copy,
  FileText,
  HeartPulse,
  Image,
  Mic,
  Paperclip,
  Plus,
  X,
} from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { NaaIcon } from "./NaaIcon";

const suggestions = [
  {
    prompt: "抽痰時血氧下降該怎麼處理？",
    label: "臨床處置",
    icon: HeartPulse,
    tone: "blue",
  },
  {
    prompt: "臨床給藥時有哪些重要注意事項？",
    label: "給藥安全",
    icon: NaaIcon,
    tone: "coral",
  },
  {
    prompt: "請幫我整理交班時需要注意的重點。",
    label: "交班整理",
    icon: Plus,
    tone: "amber",
  },
  {
    prompt: "請提供常見護理操作的流程提醒。",
    label: "操作流程",
    icon: Check,
    tone: "green",
  },
] as const;

interface AssistantThreadProps {
  account: string;
}

function Welcome({ account }: AssistantThreadProps) {
  return (
    <AuiIf condition={(state) => state.thread.isEmpty}>
      <section className="thread-welcome">
        <div className="thread-welcome__glow" aria-hidden="true" />
        <div className="thread-welcome__mark" aria-hidden="true">
          <NaaIcon size={42} />
        </div>
        <p>嗨，{account}</p>
        <h1>今天想從哪裡開始？</h1>
        <div className="suggestion-grid" aria-label="建議問題">
          {suggestions.map(({ prompt, label, icon: Icon, tone }) => (
            <ThreadPrimitive.Suggestion
              key={prompt}
              prompt={prompt}
              send
              className="suggestion-card micro-button"
            >
              <span>
                <small>{label}</small>
                <strong>{prompt}</strong>
              </span>
              <span className={`suggestion-card__icon suggestion-card__icon--${tone}`}>
                <Icon size={17} aria-hidden="true" />
              </span>
            </ThreadPrimitive.Suggestion>
          ))}
        </div>
      </section>
    </AuiIf>
  );
}

function UserMessage() {
  return (
    <MessagePrimitive.Root className="message message--user">
      <div className="message__bubble">
        <MessagePrimitive.Parts />
      </div>
    </MessagePrimitive.Root>
  );
}

function CopyMessageButton() {
  const aui = useAui();
  const [copied, setCopied] = useState(false);

  async function copyMessage() {
    await navigator.clipboard.writeText(aui.message().getCopyText());
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1400);
  }

  return (
    <button type="button" className="message__action micro-button" onClick={copyMessage} aria-label="複製回覆">
      {copied ? <Check size={15} /> : <Copy size={15} />}
      <span>{copied ? "已複製" : "複製"}</span>
    </button>
  );
}

function AssistantMessage() {
  return (
    <MessagePrimitive.Root className="message message--assistant">
      <div className="message__avatar" aria-hidden="true">
        <NaaIcon size={27} />
      </div>
      <div className="message__body">
        <div className="message__author">小護天使</div>
        <div className="message__content">
          <MessagePrimitive.Parts />
        </div>
        <div className="message__actions">
          <CopyMessageButton />
        </div>
      </div>
    </MessagePrimitive.Root>
  );
}

function ThinkingMessage() {
  return (
    <AuiIf condition={(state) => state.thread.isRunning}>
      <div className="message message--assistant message--thinking" aria-live="polite">
        <div className="message__avatar" aria-hidden="true">
          <NaaIcon size={27} />
        </div>
        <div className="thinking-dots" aria-label="小護天使正在思考">
          <i />
          <i />
          <i />
        </div>
      </div>
    </AuiIf>
  );
}

function Composer() {
  const [attachmentMenuOpen, setAttachmentMenuOpen] = useState(false);
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const attachmentControlRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!attachmentMenuOpen) return;

    const closeOnOutsideClick = (event: PointerEvent) => {
      if (!attachmentControlRef.current?.contains(event.target as Node)) {
        setAttachmentMenuOpen(false);
      }
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setAttachmentMenuOpen(false);
    };

    document.addEventListener("pointerdown", closeOnOutsideClick);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsideClick);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [attachmentMenuOpen]);

  function addSelectedFiles(files: FileList | null) {
    if (!files?.length) return;
    setSelectedFiles((current) => {
      const next = [...current, ...Array.from(files)];
      return next.filter(
        (file, index, all) =>
          all.findIndex(
            (candidate) =>
              candidate.name === file.name &&
              candidate.size === file.size &&
              candidate.lastModified === file.lastModified,
          ) === index,
      );
    });
    setAttachmentMenuOpen(false);
  }

  function formatFileSize(size: number) {
    if (size < 1024) return `${size} B`;
    if (size < 1024 * 1024) return `${Math.round(size / 1024)} KB`;
    return `${(size / (1024 * 1024)).toFixed(1)} MB`;
  }

  return (
    <div className="composer-wrap">
      <ThreadPrimitive.ScrollToBottom className="scroll-bottom" aria-label="捲動至最新訊息">
        <ArrowDown size={17} />
      </ThreadPrimitive.ScrollToBottom>
      {selectedFiles.length > 0 && (
        <div className="composer-attachments" aria-label="已選取的附件">
          {selectedFiles.map((file) => {
            const isImage = file.type.startsWith("image/");
            return (
              <div className="composer-attachment" key={`${file.name}-${file.size}-${file.lastModified}`}>
                <span className="composer-attachment__icon" aria-hidden="true">
                  {isImage ? <Image size={16} /> : <FileText size={16} />}
                </span>
                <span className="composer-attachment__copy">
                  <strong>{file.name}</strong>
                  <small>{formatFileSize(file.size)}</small>
                </span>
                <button
                  type="button"
                  className="composer-attachment__remove micro-button"
                  onClick={() =>
                    setSelectedFiles((current) => current.filter((candidate) => candidate !== file))
                  }
                  aria-label={`移除 ${file.name}`}
                >
                  <X size={14} />
                </button>
              </div>
            );
          })}
        </div>
      )}
      <ComposerPrimitive.Root className="composer">
        <div className="composer__attachment-control" ref={attachmentControlRef}>
          {attachmentMenuOpen && (
            <div className="attachment-menu" role="dialog" aria-label="新增附件">
              <button
                type="button"
                className="attachment-menu__item micro-button micro-button--icon-shift"
                onClick={() => fileInputRef.current?.click()}
              >
                <span className="attachment-menu__icon"><Paperclip size={18} /></span>
                <span>
                  <strong>新增相片與檔案</strong>
                  <small>從電腦選擇圖片、PDF 或文件</small>
                </span>
              </button>
              <p>附件會顯示在訊息列；內容上傳需串接後端附件 API。</p>
            </div>
          )}
          <input
            ref={fileInputRef}
            className="composer__file-input"
            type="file"
            multiple
            accept="image/*,.pdf,.txt,.doc,.docx,.xls,.xlsx,.ppt,.pptx"
            onChange={(event) => {
              addSelectedFiles(event.currentTarget.files);
              event.currentTarget.value = "";
            }}
          />
          <button
            className={`composer__utility composer__add micro-button ${attachmentMenuOpen ? "is-open" : ""}`}
            type="button"
            onClick={() => setAttachmentMenuOpen((open) => !open)}
            aria-label="新增相片與檔案"
            aria-expanded={attachmentMenuOpen}
            aria-haspopup="dialog"
          >
            <Plus size={20} />
          </button>
        </div>
        <ComposerPrimitive.Input
          className="composer__input"
          rows={1}
          autoFocus
          placeholder="詢問小護天使"
          aria-label="訊息輸入"
        />
        <span className="composer__model">NAA</span>
        <button className="composer__utility" type="button" disabled aria-label="語音輸入（尚未啟用）">
          <Mic size={18} />
        </button>
        <ComposerPrimitive.Send
          className="composer__send micro-button micro-button--glare"
          onClick={() => setSelectedFiles([])}
          aria-label="送出訊息"
        >
          <ArrowUp size={19} />
        </ComposerPrimitive.Send>
      </ComposerPrimitive.Root>
      <p>小護天使可能會產生不準確的資訊，重要臨床決策請依院內規範確認。</p>
    </div>
  );
}

export function AssistantThread({ account }: AssistantThreadProps) {
  return (
    <ThreadPrimitive.Root className="thread">
      <ThreadPrimitive.Viewport
        className="thread__viewport"
        autoScroll
        scrollToBottomOnRunStart
        scrollToBottomOnThreadSwitch
      >
        <div className="thread__content">
          <Welcome account={account} />
          <div className="message-list">
            <ThreadPrimitive.Messages>
              {({ message }) => {
                if (message.role === "user") return <UserMessage />;

                // assistant-ui inserts an optimistic assistant message while a
                // request is running. Keep it hidden so the dedicated animated
                // three-dot row is the only pending-response indicator.
                if (message.status?.type !== "complete") return null;

                return <AssistantMessage />;
              }}
            </ThreadPrimitive.Messages>
            <ThinkingMessage />
          </div>
        </div>
      </ThreadPrimitive.Viewport>
      <div className="thread__footer">
        <Composer />
      </div>
    </ThreadPrimitive.Root>
  );
}
