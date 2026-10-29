import { lazy, Suspense, type FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { ArrowRight, KeyRound, LockKeyhole, Nfc, UserRound } from "lucide-react";
import { stripCardInput, useCardReader } from "../cardReader";
import { Brand } from "./Brand";
import { GlassCard } from "./GlassCard";
import { LogoLoop, type LogoLoopItem } from "./LogoLoop";

const GradientWaves = lazy(() => import("./GradientWaves"));

const technologyLogos: readonly LogoLoopItem[] = [
  {
    src: "https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/react/react-original.svg",
    alt: "React",
    title: "React",
    href: "https://react.dev/",
  },
  {
    src: "https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/typescript/typescript-original.svg",
    alt: "TypeScript",
    title: "TypeScript",
    href: "https://www.typescriptlang.org/",
  },
  {
    src: "https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/vitejs/vitejs-original.svg",
    alt: "Vite",
    title: "Vite",
    href: "https://vite.dev/",
  },
  {
    src: "https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/threejs/threejs-original.svg",
    alt: "Three.js",
    title: "Three.js",
    href: "https://threejs.org/",
  },
  {
    src: "https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/csharp/csharp-original.svg",
    alt: "C Sharp",
    title: "C#",
    href: "https://learn.microsoft.com/dotnet/csharp/",
  },
  {
    src: "https://cdn.jsdelivr.net/gh/devicons/devicon@latest/icons/dotnetcore/dotnetcore-original.svg",
    alt: "ASP.NET Core",
    title: "ASP.NET Core",
    href: "https://dotnet.microsoft.com/apps/aspnet",
  },
  {
    src: "https://www.assistant-ui.com/favicon.ico",
    alt: "assistant-ui",
    title: "assistant-ui",
    href: "https://www.assistant-ui.com/",
  },
  {
    src: "https://reactbits.dev/favicon.ico",
    alt: "React Bits",
    title: "React Bits",
    href: "https://reactbits.dev/",
  },
  {
    src: "https://magicui.design/favicon.ico",
    alt: "Magic UI",
    title: "Magic UI",
    href: "https://magicui.design/",
  },
];

interface LoginPageProps {
  onLogin: (account: string, password: string) => Promise<void>;
  onCardLogin: (cardUid: string) => Promise<void>;
}

type LoginMode = "card" | "password";
type CardStatus = "idle" | "verifying" | "error";

const CARD_ERROR_RESET_MS = 5_000;

function readWindowFocus(): boolean {
  return typeof document === "undefined" || document.hasFocus();
}

export function LoginPage({ onLogin, onCardLogin }: LoginPageProps) {
  const [mode, setMode] = useState<LoginMode>("card");
  const [account, setAccount] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [cardStatus, setCardStatus] = useState<CardStatus>("idle");
  const [cardMessage, setCardMessage] = useState("");
  const [hasWindowFocus, setHasWindowFocus] = useState(readWindowFocus);
  const [showAnimatedBackground, setShowAnimatedBackground] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const isSubmittingRef = useRef(false);

  useEffect(() => {
    if (mode === "password") inputRef.current?.focus();
  }, [mode]);

  // The reader types into whichever window has keyboard focus. Through Citrix
  // that is easy to lose, so the card panel says so instead of silently waiting.
  useEffect(() => {
    const update = () => setHasWindowFocus(readWindowFocus());
    window.addEventListener("focus", update);
    window.addEventListener("blur", update);
    const intervalId = window.setInterval(update, 1_000);
    return () => {
      window.removeEventListener("focus", update);
      window.removeEventListener("blur", update);
      window.clearInterval(intervalId);
    };
  }, []);

  useEffect(() => {
    if (cardStatus !== "error") return;
    const timeoutId = window.setTimeout(() => setCardStatus("idle"), CARD_ERROR_RESET_MS);
    return () => window.clearTimeout(timeoutId);
  }, [cardStatus, cardMessage]);

  const handleCard = useCallback(
    async (cardUid: string) => {
      if (isSubmittingRef.current) return;

      // If a text field had focus, the reader also typed the UID into it.
      setAccount((value) => stripCardInput(value, cardUid));
      setPassword((value) => stripCardInput(value, cardUid));
      setMode("card");
      setError("");
      setCardMessage("");
      setCardStatus("verifying");
      isSubmittingRef.current = true;
      setIsSubmitting(true);

      try {
        await onCardLogin(cardUid);
      } catch (loginError) {
        setCardMessage(loginError instanceof Error ? loginError.message : "卡片登入失敗，請稍後再試。");
        setCardStatus("error");
      } finally {
        isSubmittingRef.current = false;
        setIsSubmitting(false);
      }
    },
    [onCardLogin],
  );

  useCardReader((cardUid) => void handleCard(cardUid));

  useEffect(() => {
    const browserWindow = window as Window & {
      requestIdleCallback?: typeof window.requestIdleCallback;
      cancelIdleCallback?: typeof window.cancelIdleCallback;
    };

    if (browserWindow.requestIdleCallback && browserWindow.cancelIdleCallback) {
      const idleId = browserWindow.requestIdleCallback(
        () => setShowAnimatedBackground(true),
        { timeout: 1_500 },
      );
      return () => browserWindow.cancelIdleCallback?.(idleId);
    }

    const timeoutId = setTimeout(() => setShowAnimatedBackground(true), 250);
    return () => clearTimeout(timeoutId);
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const value = account.trim();
    if (!value) {
      setError("請輸入員工帳號");
      inputRef.current?.focus();
      return;
    }

    if (!password) {
      setError("請輸入密碼");
      passwordRef.current?.focus();
      return;
    }

    setError("");
    isSubmittingRef.current = true;
    setIsSubmitting(true);
    try {
      await onLogin(value, password);
    } catch (loginError) {
      setError(loginError instanceof Error ? loginError.message : "登入失敗，請稍後再試。");
      setPassword("");
      passwordRef.current?.focus();
    } finally {
      isSubmittingRef.current = false;
      setIsSubmitting(false);
    }
  }

  function switchMode() {
    setError("");
    setCardStatus("idle");
    setCardMessage("");
    setMode((current) => (current === "card" ? "password" : "card"));
  }

  const cardTitle =
    cardStatus === "verifying"
      ? "驗證中…"
      : cardStatus === "error"
        ? "無法登入"
        : hasWindowFocus
          ? "請感應員工卡"
          : "請先點一下這個畫面";
  const cardDetail =
    cardStatus === "verifying"
      ? "正在確認卡片綁定的帳號"
      : cardStatus === "error"
        ? cardMessage
        : hasWindowFocus
          ? "將卡片靠近讀卡機，系統會自動登入"
          : "讀卡機的輸入只會送到目前選取的視窗";

  return (
    <main className="login-page">
      <div className="login-page__waves" aria-hidden="true">
        {showAnimatedBackground ? (
          <Suspense fallback={<div className="gradient-waves-fallback" />}>
            <GradientWaves
              horizonColor="#f0ecff"
              waveColor="#db77d8"
              crestColor="#eeabab"
            />
          </Suspense>
        ) : (
          <div className="gradient-waves-fallback" />
        )}
      </div>
      <div className="login-page__veil" aria-hidden="true" />

      <GlassCard className="login-card" aria-labelledby="login-title">
        <Brand className="login-card__brand" />

        <form className="login-form" onSubmit={submit} noValidate>
          <div className="login-form__eyebrow">WELCOME BACK</div>
          <h1 id="login-title">歡迎回來</h1>
          <p>
            {mode === "card"
              ? "感應員工卡即可登入，繼續你的護理 AI 對話。"
              : "使用員工帳號登入，繼續你的護理 AI 對話。"}
          </p>

          {mode === "card" ? (
            <div
              className={`card-tap card-tap--${cardStatus} ${
                !hasWindowFocus && cardStatus === "idle" ? "card-tap--unfocused" : ""
              }`}
              role="status"
              aria-live="polite"
            >
              <div className="card-tap__visual" aria-hidden="true">
                <span className="card-tap__ring" />
                <span className="card-tap__ring card-tap__ring--delayed" />
                <span className="card-tap__icon">
                  {cardStatus === "verifying" ? <span className="card-tap__spinner" /> : <Nfc size={30} />}
                </span>
              </div>
              <strong>{cardTitle}</strong>
              <span>{cardDetail}</span>
            </div>
          ) : (
            <>
              <label htmlFor="account">員工帳號</label>
              <div className={`login-input ${error ? "login-input--error" : ""}`}>
                <UserRound size={20} aria-hidden="true" />
                <input
                  ref={inputRef}
                  id="account"
                  name="account"
                  type="text"
                  value={account}
                  onChange={(event) => {
                    setAccount(event.target.value);
                    if (error) setError("");
                  }}
                  autoComplete="username"
                  placeholder="例如：N001"
                  aria-describedby={error ? "account-error" : undefined}
                  aria-invalid={Boolean(error)}
                />
              </div>

              <label htmlFor="password">密碼</label>
              <div className={`login-input ${error ? "login-input--error" : ""}`}>
                <LockKeyhole size={20} aria-hidden="true" />
                <input
                  ref={passwordRef}
                  id="password"
                  name="password"
                  type="password"
                  value={password}
                  onChange={(event) => {
                    setPassword(event.target.value);
                    if (error) setError("");
                  }}
                  autoComplete="current-password"
                  placeholder="請輸入密碼"
                  aria-describedby={error ? "account-error" : undefined}
                  aria-invalid={Boolean(error)}
                />
              </div>

              {error && (
                <div id="account-error" className="login-form__error" role="alert">
                  {error}
                </div>
              )}

              <button className="login-button" type="submit" disabled={isSubmitting}>
                <span>{isSubmitting ? "登入中…" : "進入系統"}</span>
                <ArrowRight size={20} aria-hidden="true" />
              </button>
            </>
          )}

          <button className="login-switch" type="button" onClick={switchMode} disabled={isSubmitting}>
            {mode === "card" ? <KeyRound size={16} aria-hidden="true" /> : <Nfc size={16} aria-hidden="true" />}
            <span>{mode === "card" ? "改用帳號密碼登入" : "改用刷卡登入"}</span>
          </button>
        </form>

        <footer>NAA · Nursing AI Assistant</footer>
      </GlassCard>

      <div className="login-tech" aria-label="本系統使用技術">
        <span className="login-tech__label">BUILT WITH</span>
        <LogoLoop
          logos={technologyLogos}
          speed={42}
          logoHeight={30}
          gap={58}
          pauseOnHover
          fadeOut
          scaleOnHover
          ariaLabel="本系統使用技術商標"
        />
      </div>
    </main>
  );
}
