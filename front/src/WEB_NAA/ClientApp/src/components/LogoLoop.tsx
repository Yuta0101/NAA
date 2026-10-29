import {
  memo,
  type CSSProperties,
  type ReactNode,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";

export type LogoLoopItem =
  | {
      src: string;
      alt: string;
      title?: string;
      href?: string;
    }
  | {
      node: ReactNode;
      title: string;
      href?: string;
    };

interface LogoLoopProps {
  logos: readonly LogoLoopItem[];
  speed?: number;
  direction?: "left" | "right";
  logoHeight?: number;
  gap?: number;
  pauseOnHover?: boolean;
  hoverSpeed?: number;
  fadeOut?: boolean;
  scaleOnHover?: boolean;
  ariaLabel?: string;
  className?: string;
  style?: CSSProperties;
}

type LoopStyle = CSSProperties & {
  "--logoloop-gap": string;
  "--logoloop-logo-height": string;
};

const MIN_COPIES = 2;
const COPY_HEADROOM = 2;
const SMOOTH_TAU = 0.25;

export const LogoLoop = memo(function LogoLoop({
  logos,
  speed = 120,
  direction = "left",
  logoHeight = 28,
  gap = 32,
  pauseOnHover = true,
  hoverSpeed,
  fadeOut = false,
  scaleOnHover = false,
  ariaLabel = "使用技術",
  className = "",
  style,
}: LogoLoopProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const trackRef = useRef<HTMLDivElement>(null);
  const sequenceRef = useRef<HTMLUListElement>(null);
  const [sequenceWidth, setSequenceWidth] = useState(0);
  const [copyCount, setCopyCount] = useState(MIN_COPIES);
  const hoveredRef = useRef(false);

  const updateDimensions = useCallback(() => {
    const containerWidth = containerRef.current?.clientWidth ?? 0;
    const width = Math.ceil(sequenceRef.current?.getBoundingClientRect().width ?? 0);
    if (!width) return;
    const nextCopyCount = Math.max(
      MIN_COPIES,
      Math.ceil(containerWidth / width) + COPY_HEADROOM,
    );
    setSequenceWidth((current) => (current === width ? current : width));
    setCopyCount((current) => (current === nextCopyCount ? current : nextCopyCount));
  }, []);

  useEffect(() => {
    updateDimensions();
    const observer = new ResizeObserver(updateDimensions);
    if (containerRef.current) observer.observe(containerRef.current);
    if (sequenceRef.current) observer.observe(sequenceRef.current);
    window.addEventListener("load", updateDimensions);
    return () => {
      observer.disconnect();
      window.removeEventListener("load", updateDimensions);
    };
  }, [logos, gap, logoHeight, updateDimensions]);

  useEffect(() => {
    const track = trackRef.current;
    if (!track || sequenceWidth <= 0) return;

    let animationFrame = 0;
    let lastTimestamp: number | null = null;
    let offset = 0;
    let velocity = 0;
    let visible = true;
    let documentVisible = !document.hidden;
    const baseVelocity = Math.abs(speed) * (direction === "left" ? 1 : -1);
    const hoverVelocity = hoverSpeed ?? (pauseOnHover ? 0 : baseVelocity);

    const animate = (timestamp: number) => {
      if (lastTimestamp === null) lastTimestamp = timestamp;
      const delta = Math.max(0, timestamp - lastTimestamp) / 1000;
      lastTimestamp = timestamp;
      const targetVelocity = hoveredRef.current ? hoverVelocity : baseVelocity;
      velocity += (targetVelocity - velocity) * (1 - Math.exp(-delta / SMOOTH_TAU));
      offset = ((offset + velocity * delta) % sequenceWidth + sequenceWidth) % sequenceWidth;
      track.style.transform = `translate3d(${-offset}px, 0, 0)`;
      animationFrame = requestAnimationFrame(animate);
    };

    const start = () => {
      if (visible && documentVisible && animationFrame === 0) {
        lastTimestamp = null;
        animationFrame = requestAnimationFrame(animate);
      }
    };
    const stop = () => {
      if (animationFrame === 0) return;
      cancelAnimationFrame(animationFrame);
      animationFrame = 0;
      lastTimestamp = null;
    };

    const observer = new IntersectionObserver(([entry]) => {
      visible = entry.isIntersecting;
      if (visible) start();
      else stop();
    });
    observer.observe(track);

    const onVisibilityChange = () => {
      documentVisible = !document.hidden;
      if (documentVisible) start();
      else stop();
    };
    document.addEventListener("visibilitychange", onVisibilityChange);
    start();

    return () => {
      stop();
      observer.disconnect();
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [direction, hoverSpeed, pauseOnHover, sequenceWidth, speed]);

  const rootClassName = [
    "logoloop",
    fadeOut && "logoloop--fade",
    scaleOnHover && "logoloop--scale-hover",
    className,
  ]
    .filter(Boolean)
    .join(" ");

  const rootStyle = useMemo<LoopStyle>(
    () => ({
      "--logoloop-gap": `${gap}px`,
      "--logoloop-logo-height": `${logoHeight}px`,
      ...style,
    }),
    [gap, logoHeight, style],
  );

  function renderLogo(item: LogoLoopItem, key: string) {
    const content = "node" in item ? (
      <span className="logoloop__node" aria-hidden="true">
        {item.node}
      </span>
    ) : (
      <img
        src={item.src}
        alt={item.alt}
        title={item.title ?? item.alt}
        loading="lazy"
        decoding="async"
        fetchPriority="low"
        draggable={false}
      />
    );

    return (
      <li className="logoloop__item" key={key}>
        {item.href ? (
          <a
            className="logoloop__link"
            href={item.href}
            target="_blank"
            rel="noreferrer noopener"
            aria-label={`前往 ${item.title} 官網`}
            title={`前往 ${item.title} 官網`}
          >
            {content}
          </a>
        ) : (
          content
        )}
      </li>
    );
  }

  return (
    <div
      ref={containerRef}
      className={rootClassName}
      style={rootStyle}
      role="region"
      aria-label={ariaLabel}
    >
      <div
        className="logoloop__track"
        ref={trackRef}
        onMouseEnter={() => {
          hoveredRef.current = true;
        }}
        onMouseLeave={() => {
          hoveredRef.current = false;
        }}
      >
        {Array.from({ length: copyCount }, (_, copyIndex) => (
          <ul
            className="logoloop__list"
            key={`copy-${copyIndex}`}
            aria-hidden={copyIndex > 0}
            ref={copyIndex === 0 ? sequenceRef : undefined}
          >
            {logos.map((item, itemIndex) =>
              renderLogo(item, `${copyIndex}-${itemIndex}`),
            )}
          </ul>
        ))}
      </div>
    </div>
  );
});
