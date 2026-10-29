import { useEffect, useRef } from "react";

/**
 * Detects NFC card reads from a keyboard-emulation reader (for example YP201).
 *
 * The reader "types" the card UID and presses Enter. Through Citrix the gap
 * between keys measured 0-18 ms, while a person typing measured 87-125 ms on
 * average. A burst counts as a card only when every gap, including the gap
 * before Enter, is within maxGapMs, so normal typing never triggers a login.
 *
 * Characters are read from KeyboardEvent.code (the physical key) instead of
 * KeyboardEvent.key, so a Chinese input method cannot change the digits.
 */
export interface CardReaderOptions {
  /** Largest allowed gap between two keys of the same card read. */
  maxGapMs: number;
  minLength: number;
  maxLength: number;
}

export const DEFAULT_CARD_READER_OPTIONS: CardReaderOptions = {
  maxGapMs: 60,
  minLength: 8,
  maxLength: 20,
};

export interface CardKeyEvent {
  code: string;
  timeStamp: number;
  repeat?: boolean;
  ctrlKey?: boolean;
  altKey?: boolean;
  metaKey?: boolean;
}

const IGNORED_CODES = new Set([
  "ShiftLeft",
  "ShiftRight",
  "CapsLock",
  "NumLock",
]);

function codeToCardChar(code: string): string | undefined {
  if (/^Digit[0-9]$/.test(code)) return code.slice(5);
  if (/^Numpad[0-9]$/.test(code)) return code.slice(6);
  // Some readers send the UID as hexadecimal.
  if (/^Key[A-F]$/.test(code)) return code.slice(3);
  return undefined;
}

function isEnter(code: string): boolean {
  return code === "Enter" || code === "NumpadEnter";
}

export interface CardBurstDetector {
  /** Returns the UID when this key completes a card read, otherwise undefined. */
  push(event: CardKeyEvent): string | undefined;
  reset(): void;
}

export function createCardBurstDetector(
  options: CardReaderOptions = DEFAULT_CARD_READER_OPTIONS,
): CardBurstDetector {
  let buffer = "";
  let lastTime = Number.NEGATIVE_INFINITY;

  const reset = () => {
    buffer = "";
    lastTime = Number.NEGATIVE_INFINITY;
  };

  return {
    reset,
    push(event) {
      if (event.repeat || event.ctrlKey || event.altKey || event.metaKey) {
        reset();
        return undefined;
      }
      if (IGNORED_CODES.has(event.code)) return undefined;

      const gap = event.timeStamp - lastTime;

      if (isEnter(event.code)) {
        const candidate = buffer;
        const inTime = gap >= 0 && gap <= options.maxGapMs;
        reset();
        if (inTime && candidate.length >= options.minLength && candidate.length <= options.maxLength) {
          return candidate;
        }
        return undefined;
      }

      const character = codeToCardChar(event.code);
      if (character === undefined) {
        reset();
        return undefined;
      }

      // A slow key starts a new burst; it may be the first key of a card read.
      if (!(gap >= 0 && gap <= options.maxGapMs)) buffer = "";
      buffer += character;
      lastTime = event.timeStamp;
      if (buffer.length > options.maxLength) buffer = buffer.slice(-options.maxLength);
      return undefined;
    },
  };
}

/** Removes the characters a card read typed into a focused text field. */
export function stripCardInput(value: string, uid: string): string {
  return value.toUpperCase().endsWith(uid.toUpperCase()) ? value.slice(0, -uid.length) : value;
}

/**
 * Listens for card reads on the whole page while enabled. The Enter key that
 * ends a card read is cancelled, so it does not also submit a form or press a
 * focused button.
 */
export function useCardReader(
  onCard: (uid: string) => void,
  enabled = true,
  options: CardReaderOptions = DEFAULT_CARD_READER_OPTIONS,
): void {
  const onCardRef = useRef(onCard);
  onCardRef.current = onCard;

  const { maxGapMs, minLength, maxLength } = options;

  useEffect(() => {
    if (!enabled) return;
    const detector = createCardBurstDetector({ maxGapMs, minLength, maxLength });

    const handleKeyDown = (event: KeyboardEvent) => {
      const uid = detector.push(event);
      if (!uid) return;
      event.preventDefault();
      event.stopPropagation();
      onCardRef.current(uid);
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => window.removeEventListener("keydown", handleKeyDown, true);
  }, [enabled, maxGapMs, minLength, maxLength]);
}
