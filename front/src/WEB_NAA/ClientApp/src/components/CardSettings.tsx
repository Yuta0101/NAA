import { CreditCard, Nfc, Plus, Unlink, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { bindCard, getCards, unbindCard } from "../api/ddm";
import { useCardReader } from "../cardReader";
import type { BoundCard } from "../types";

type Notice = { tone: "success" | "error"; text: string };

function formatBoundAt(date?: Date): string {
  if (!date) return "";
  return `綁定於 ${date.toLocaleDateString("zh-TW", { year: "numeric", month: "2-digit", day: "2-digit" })}`;
}

/**
 * Lists the cards bound to the signed-in account and binds a new card by
 * tapping it while "waiting for card" is shown. The account is never sent
 * from here: the Front proxy fills it in from the login cookie.
 */
export function CardSettings() {
  const [cards, setCards] = useState<BoundCard[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isWaitingForCard, setIsWaitingForCard] = useState(false);
  const [isBusy, setIsBusy] = useState(false);
  const [notice, setNotice] = useState<Notice>();
  const busyRef = useRef(false);

  const refresh = useCallback(async () => {
    setIsLoading(true);
    try {
      setCards(await getCards());
    } catch (error) {
      setNotice({ tone: "error", text: error instanceof Error ? error.message : "無法載入卡片。" });
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const handleCard = useCallback(
    async (cardUid: string) => {
      if (busyRef.current) return;
      busyRef.current = true;
      setIsBusy(true);
      setNotice(undefined);
      try {
        const message = await bindCard(cardUid);
        setNotice({ tone: "success", text: message });
        setIsWaitingForCard(false);
        await refresh();
      } catch (error) {
        setNotice({ tone: "error", text: error instanceof Error ? error.message : "卡片綁定失敗。" });
      } finally {
        busyRef.current = false;
        setIsBusy(false);
      }
    },
    [refresh],
  );

  useCardReader((cardUid) => void handleCard(cardUid), isWaitingForCard);

  async function removeCard(card: BoundCard) {
    if (busyRef.current) return;
    busyRef.current = true;
    setIsBusy(true);
    setNotice(undefined);
    try {
      await unbindCard(card.cardId);
      setNotice({ tone: "success", text: "已解除卡片綁定" });
      await refresh();
    } catch (error) {
      setNotice({ tone: "error", text: error instanceof Error ? error.message : "解除綁定失敗。" });
    } finally {
      busyRef.current = false;
      setIsBusy(false);
    }
  }

  return (
    <section className="card-settings" aria-labelledby="card-settings-title">
      <div className="card-settings__head">
        <strong id="card-settings-title">刷卡登入</strong>
        {!isWaitingForCard && (
          <button
            type="button"
            className="card-settings__add micro-button"
            onClick={() => {
              setNotice(undefined);
              setIsWaitingForCard(true);
            }}
            disabled={isBusy}
          >
            <Plus size={14} aria-hidden="true" />
            <span>綁定新卡片</span>
          </button>
        )}
      </div>

      {isWaitingForCard && (
        <div className="card-settings__waiting" role="status" aria-live="polite">
          <span className="card-settings__pulse" aria-hidden="true">
            <Nfc size={18} />
          </span>
          <span>{isBusy ? "綁定中…" : "請感應新卡片…"}</span>
          <button
            type="button"
            className="micro-button"
            onClick={() => setIsWaitingForCard(false)}
            aria-label="取消綁定"
            disabled={isBusy}
          >
            <X size={15} />
          </button>
        </div>
      )}

      {isLoading ? (
        <p className="card-settings__empty">載入中…</p>
      ) : cards.length === 0 ? (
        <p className="card-settings__empty">尚未綁定卡片，綁定後即可在登入頁直接刷卡登入。</p>
      ) : (
        <ul className="card-settings__list">
          {cards.map((card) => (
            <li key={card.cardId}>
              <CreditCard size={17} aria-hidden="true" />
              <span>
                <strong>{card.maskedUid}</strong>
                <small>{formatBoundAt(card.boundAt)}</small>
              </span>
              <button
                type="button"
                className="micro-button"
                onClick={() => void removeCard(card)}
                disabled={isBusy}
                aria-label={`解除卡片 ${card.maskedUid}`}
                title="解除綁定"
              >
                <Unlink size={15} />
              </button>
            </li>
          ))}
        </ul>
      )}

      {notice && (
        <p className={`card-settings__notice card-settings__notice--${notice.tone}`} role="alert">
          {notice.text}
        </p>
      )}
    </section>
  );
}
