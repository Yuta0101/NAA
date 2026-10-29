import { lazy, Suspense, useEffect, useLayoutEffect, useState } from "react";
import { authenticateCard, authenticateUser, getSessionAccount, logoutUser } from "./api/ddm";
import type { AppTheme } from "./components/AnimatedThemeToggler";
import { NaaIcon } from "./components/NaaIcon";

const loadChatPage = () =>
  import("./components/ChatPage").then((module) => ({ default: module.ChatPage }));
const ChatPage = lazy(loadChatPage);
const LoginPage = lazy(() =>
  import("./components/LoginPage").then((module) => ({ default: module.LoginPage })),
);

const THEME_KEY = "naa.theme";

function readTheme(): AppTheme {
  try {
    const savedTheme = window.localStorage.getItem(THEME_KEY);
    if (savedTheme === "light" || savedTheme === "dark") return savedTheme;
  } catch {
    // The browser can block local storage; system preference remains a safe fallback.
  }
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export default function App() {
  const [account, setAccount] = useState("");
  const [isSessionLoading, setIsSessionLoading] = useState(true);
  const [theme, setTheme] = useState<AppTheme>(readTheme);

  useEffect(() => {
    let active = true;
    void getSessionAccount()
      .then((sessionAccount) => {
        if (active) setAccount(sessionAccount || "");
      })
      .finally(() => {
        if (active) setIsSessionLoading(false);
      });
    return () => {
      active = false;
    };
  }, []);

  useLayoutEffect(() => {
    document.documentElement.classList.toggle("dark", theme === "dark");
    document.documentElement.style.colorScheme = theme;
    try {
      window.localStorage.setItem(THEME_KEY, theme);
    } catch {
      // Theme still works for the current page when persistence is unavailable.
    }
  }, [theme]);

  async function login(value: string, password: string) {
    // Load the authenticated screen while the credential request is in flight.
    void loadChatPage();
    const authenticatedAccount = await authenticateUser(value, password);
    setAccount(authenticatedAccount);
  }

  async function loginWithCard(cardUid: string) {
    void loadChatPage();
    const authenticatedAccount = await authenticateCard(cardUid);
    setAccount(authenticatedAccount);
  }

  function logout() {
    setAccount("");
    void logoutUser();
  }

  return (
    <Suspense
      fallback={
        <div className="app-loading" role="status">
          <NaaIcon size={58} aria-hidden="true" />
          <p>正在載入小護天使…</p>
        </div>
      }
    >
      {isSessionLoading ? (
        <div className="app-loading" role="status">
          <NaaIcon size={58} aria-hidden="true" />
          <p>正在確認登入狀態…</p>
        </div>
      ) : account ? (
        <ChatPage
          account={account}
          theme={theme}
          onThemeChange={setTheme}
          onLogout={logout}
        />
      ) : (
        <LoginPage onLogin={login} onCardLogin={loginWithCard} />
      )}
    </Suspense>
  );
}
