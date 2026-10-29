import { type CSSProperties, type HTMLAttributes, type PointerEvent, useEffect, useRef } from "react";

type GlassCardStyle = CSSProperties & {
  "--glass-pointer-x"?: string;
  "--glass-pointer-y"?: string;
};

export function GlassCard({
  className = "",
  children,
  style,
  onPointerMove,
  onPointerLeave,
  ...props
}: HTMLAttributes<HTMLElement>) {
  const frameRef = useRef(0);
  const pointerRef = useRef<{
    element: HTMLElement;
    clientX: number;
    clientY: number;
  } | null>(null);

  useEffect(() => () => window.cancelAnimationFrame(frameRef.current), []);

  function updateGlow(event: PointerEvent<HTMLElement>) {
    pointerRef.current = {
      element: event.currentTarget,
      clientX: event.clientX,
      clientY: event.clientY,
    };
    if (!frameRef.current) {
      frameRef.current = window.requestAnimationFrame(() => {
        frameRef.current = 0;
        const pointer = pointerRef.current;
        if (!pointer) return;
        const bounds = pointer.element.getBoundingClientRect();
        pointer.element.style.setProperty("--glass-pointer-x", `${pointer.clientX - bounds.left}px`);
        pointer.element.style.setProperty("--glass-pointer-y", `${pointer.clientY - bounds.top}px`);
      });
    }
    onPointerMove?.(event);
  }

  function resetGlow(event: PointerEvent<HTMLElement>) {
    pointerRef.current = null;
    window.cancelAnimationFrame(frameRef.current);
    frameRef.current = 0;
    event.currentTarget.style.removeProperty("--glass-pointer-x");
    event.currentTarget.style.removeProperty("--glass-pointer-y");
    onPointerLeave?.(event);
  }

  return (
    <section
      className={`glass-card ${className}`.trim()}
      style={style as GlassCardStyle}
      onPointerMove={updateGlow}
      onPointerLeave={resetGlow}
      {...props}
    >
      <span className="glass-card__noise" aria-hidden="true" />
      <span className="glass-card__shine" aria-hidden="true" />
      <div className="glass-card__content">{children}</div>
    </section>
  );
}
