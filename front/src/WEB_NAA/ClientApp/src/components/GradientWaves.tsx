import { memo, useEffect, useRef } from "react";
import * as THREE from "three";
import "./GradientWaves.css";

type Detail = "low" | "medium" | "high";

interface GradientWavesProps {
  horizonColor?: string;
  waveColor?: string;
  crestColor?: string;
  speed?: number;
  amplitude?: number;
  waveScale?: number;
  waveRatio?: number;
  swell?: number;
  turbulence?: number;
  tilt?: number;
  zoom?: number;
  height?: number;
  fogDepth?: number;
  detail?: Detail;
  brightness?: number;
  opacity?: number;
  mouseInteraction?: boolean;
  parallaxStrength?: number;
  grain?: boolean;
  grainIntensity?: number;
  className?: string;
}

const steps: Record<Detail, number> = { low: 40, medium: 70, high: 110 };
const NORMAL_FRAME_INTERVAL = 1000 / 30;
const CONSTRAINED_FRAME_INTERVAL = 1000 / 24;
const NORMAL_PIXEL_BUDGET = 1_100_000;
const CONSTRAINED_PIXEL_BUDGET = 550_000;

const vertexShader = `
  in vec3 position;
  void main() {
    gl_Position = vec4(position, 1.0);
  }
`;

const fragmentShader = `
  precision highp float;
  uniform vec2 iResolution;
  uniform float iTime;
  uniform float uSpeed;
  uniform float uAmplitude;
  uniform float uWaveScale;
  uniform float uWaveRatio;
  uniform float uSwell;
  uniform float uTurbulence;
  uniform float uTilt;
  uniform float uZoom;
  uniform float uHeight;
  uniform float uFogDepth;
  uniform float uSteps;
  uniform float uBrightness;
  uniform float uOpacity;
  uniform float uGrain;
  uniform float uGrainIntensity;
  uniform vec2 uMouse;
  uniform float uParallax;
  uniform bool uEnableMouse;
  uniform vec3 uHorizonColor;
  uniform vec3 uWaveColor;
  uniform vec3 uCrestColor;
  out vec4 fragColor;

  const float MAX_DIST = 20000.0;

  float hash21(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
  }

  float plasma(vec3 r, vec2 freq, vec4 tc) {
    float mx = r.x + tc.x;
    mx += uSwell * sin((r.y + mx) / 20.0 + tc.y);
    float my = r.y - tc.z;
    my += uTurbulence * cos(r.x / 23.0 + tc.w);
    return r.z - (sin(mx * freq.x) * uAmplitude + sin(my * freq.y) * uAmplitude + uHeight);
  }

  float raymarch(vec3 pos, vec3 dir, vec2 freq, vec4 tc) {
    float dist = 0.0;
    for (int i = 0; i < 128; i++) {
      if (float(i) >= uSteps) break;
      float dscene = plasma(pos + dist * dir, freq, tc);
      if (abs(dscene) < 0.1) break;
      dist += 0.9 * dscene;
      if (!(abs(dist) < MAX_DIST)) return MAX_DIST;
    }
    return dist;
  }

  void main() {
    float T = iTime * uSpeed;
    vec2 freq = vec2(uWaveScale / 7.0, (uWaveScale * uWaveRatio) / 3.0);
    vec4 tc = vec4(T / 0.130, T / 0.810, T / 0.200, T / 0.710);
    float c;
    float s;
    float vfov = (3.14159 / 2.3) / max(uZoom, 0.05);
    vec3 cam = vec3(0.0, 0.0, 30.0);
    vec2 uv = (gl_FragCoord.xy / iResolution.xy) - 0.5;
    uv.x *= iResolution.x / iResolution.y;
    uv.y *= -1.0;

    vec3 dir = vec3(0.0, 0.0, -1.0);
    float ulen = length(uv);
    float xrot = vfov * ulen;
    c = cos(xrot);
    s = sin(xrot);
    dir = mat3(1.0, 0.0, 0.0, 0.0, c, -s, 0.0, s, c) * dir;
    vec2 nuv = ulen > 1e-5 ? uv / ulen : vec2(1.0, 0.0);
    c = nuv.x;
    s = nuv.y;
    dir = mat3(c, -s, 0.0, s, c, 0.0, 0.0, 0.0, 1.0) * dir;
    c = cos(uTilt);
    s = sin(uTilt);
    dir = mat3(c, 0.0, s, 0.0, 1.0, 0.0, -s, 0.0, c) * dir;

    if (uEnableMouse) {
      float yaw = (uMouse.x - 0.5) * uParallax * 0.4;
      float pitch = (uMouse.y - 0.5) * uParallax * 0.4;
      c = cos(yaw);
      s = sin(yaw);
      dir = mat3(c, 0.0, s, 0.0, 1.0, 0.0, -s, 0.0, c) * dir;
      c = cos(pitch);
      s = sin(pitch);
      dir = mat3(1.0, 0.0, 0.0, 0.0, c, -s, 0.0, s, c) * dir;
    }

    float dist = raymarch(cam, dir, freq, tc);
    vec3 pos = cam + dist * dir;
    float t = clamp(uFogDepth / max(dist, 0.001), 0.0, 1.0);
    vec3 body = mix(uWaveColor, uCrestColor, clamp(pos.z * 0.08 + 0.5, 0.0, 1.0));
    vec3 col = clamp(mix(uHorizonColor, body, t) * uBrightness, 0.0, 1.0);

    float alpha = clamp(t, 0.0, 1.0) * uOpacity;
    if (uGrain > 0.5) {
      float g = hash21(gl_FragCoord.xy + mod(iTime, 64.0) * 11.0);
      alpha += (g - 0.5) * uGrainIntensity;
    }
    alpha = clamp(alpha, 0.0, 1.0);
    fragColor = vec4(col * alpha, alpha);
  }
`;

function GradientWaves({
  horizonColor = "#5227ff",
  waveColor = "#ff9ffc",
  crestColor = "#ffffff",
  speed = 0.4,
  amplitude = 2.5,
  waveScale = 0.6,
  waveRatio = 0.9,
  swell = 35,
  turbulence = 20,
  tilt = 1.11,
  zoom = 1,
  height = 5.5,
  fogDepth = 15,
  detail = "medium",
  brightness = 1,
  opacity = 1,
  mouseInteraction = true,
  parallaxStrength = 0.5,
  grain = true,
  grainIntensity = 0.05,
  className = "",
}: GradientWavesProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    let renderer: THREE.WebGLRenderer;
    try {
      renderer = new THREE.WebGLRenderer({
        alpha: true,
        antialias: false,
        premultipliedAlpha: false,
        powerPreference: "high-performance",
      });
    } catch {
      // The static CSS gradient remains visible when WebGL is unavailable.
      return;
    }
    renderer.setClearColor(0x000000, 0);

    const gl = renderer.getContext();
    const debugInfo = gl.getExtension("WEBGL_debug_renderer_info");
    const rendererName = debugInfo
      ? String(gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL))
      : "";
    const deviceMemory = (navigator as Navigator & { deviceMemory?: number }).deviceMemory;
    const constrainedDevice =
      navigator.hardwareConcurrency <= 4 ||
      (deviceMemory !== undefined && deviceMemory <= 4) ||
      /swiftshader|llvmpipe|software|microsoft basic render/i.test(rendererName);
    const frameInterval = constrainedDevice
      ? CONSTRAINED_FRAME_INTERVAL
      : NORMAL_FRAME_INTERVAL;
    const pixelBudget = constrainedDevice
      ? CONSTRAINED_PIXEL_BUDGET
      : NORMAL_PIXEL_BUDGET;

    const uniforms = {
      iTime: { value: 0 },
      iResolution: { value: new THREE.Vector2(1, 1) },
      uSpeed: { value: speed },
      uAmplitude: { value: amplitude },
      uWaveScale: { value: waveScale },
      uWaveRatio: { value: waveRatio },
      uSwell: { value: swell },
      uTurbulence: { value: turbulence },
      uTilt: { value: tilt },
      uZoom: { value: zoom },
      uHeight: { value: height },
      uFogDepth: { value: fogDepth },
      uSteps: { value: constrainedDevice ? Math.min(steps[detail], steps.low) : steps[detail] },
      uBrightness: { value: brightness },
      uOpacity: { value: opacity },
      uGrain: { value: grain ? 1 : 0 },
      uGrainIntensity: { value: grainIntensity },
      uMouse: { value: new THREE.Vector2(0.5, 0.5) },
      uParallax: { value: parallaxStrength },
      uEnableMouse: { value: mouseInteraction },
      uHorizonColor: { value: new THREE.Color(horizonColor) },
      uWaveColor: { value: new THREE.Color(waveColor) },
      uCrestColor: { value: new THREE.Color(crestColor) },
    };

    const geometry = new THREE.PlaneGeometry(2, 2);
    const material = new THREE.RawShaderMaterial({
      vertexShader,
      fragmentShader,
      uniforms,
      glslVersion: THREE.GLSL3,
      transparent: true,
      depthWrite: false,
      depthTest: false,
    });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.frustumCulled = false;
    const scene = new THREE.Scene();
    const camera = new THREE.Camera();
    scene.add(mesh);

    const canvas = renderer.domElement;
    Object.assign(canvas.style, { width: "100%", height: "100%", display: "block" });
    container.appendChild(canvas);

    const pointerTarget = new THREE.Vector2(0.5, 0.5);
    const pointerCurrent = new THREE.Vector2(0.5, 0.5);
    const drawingBufferSize = new THREE.Vector2();
    let frameId = 0;
    let visible = true;
    let documentVisible = !document.hidden;
    let lastRenderAt = 0;
    let renderedWidth = 0;
    let renderedHeight = 0;
    const startedAt = performance.now();

    const resize = () => {
      const rect = container.getBoundingClientRect();
      const width = Math.max(1, Math.floor(rect.width));
      const viewHeight = Math.max(1, Math.floor(rect.height));
      if (width === renderedWidth && viewHeight === renderedHeight) return;
      renderedWidth = width;
      renderedHeight = viewHeight;
      const budgetRatio = Math.sqrt(pixelBudget / (width * viewHeight));
      const minimumPixelRatio = constrainedDevice ? 0.5 : 0.7;
      const pixelRatio = Math.max(
        minimumPixelRatio,
        Math.min(window.devicePixelRatio || 1, 1.25, budgetRatio),
      );
      renderer.setPixelRatio(pixelRatio);
      renderer.setSize(width, viewHeight, false);
      renderer.getDrawingBufferSize(drawingBufferSize);
      uniforms.iResolution.value.copy(drawingBufferSize);
    };

    const onPointerMove = (event: PointerEvent) => {
      const rect = canvas.getBoundingClientRect();
      if (!rect.width || !rect.height) return;
      pointerTarget.set(
        (event.clientX - rect.left) / rect.width,
        1 - (event.clientY - rect.top) / rect.height,
      );
    };
    const onPointerLeave = () => pointerTarget.set(0.5, 0.5);

    const render = (time: number) => {
      if (time - lastRenderAt < frameInterval) {
        frameId = window.requestAnimationFrame(render);
        return;
      }
      lastRenderAt = time;
      uniforms.iTime.value = (time - startedAt) * 0.001;
      if (mouseInteraction) {
        pointerCurrent.lerp(pointerTarget, 0.08);
        uniforms.uMouse.value.copy(pointerCurrent);
      }
      renderer.render(scene, camera);
      frameId = window.requestAnimationFrame(render);
    };
    const start = () => {
      if (visible && documentVisible && frameId === 0) frameId = window.requestAnimationFrame(render);
    };
    const stop = () => {
      if (frameId === 0) return;
      window.cancelAnimationFrame(frameId);
      frameId = 0;
    };

    const resizeObserver = new ResizeObserver(resize);
    resizeObserver.observe(container);
    const intersectionObserver = new IntersectionObserver(([entry]) => {
      visible = entry.isIntersecting;
      if (visible) start();
      else stop();
    });
    intersectionObserver.observe(container);
    const onVisibilityChange = () => {
      documentVisible = !document.hidden;
      if (documentVisible) start();
      else stop();
    };

    canvas.addEventListener("pointermove", onPointerMove, { passive: true });
    canvas.addEventListener("pointerleave", onPointerLeave);
    document.addEventListener("visibilitychange", onVisibilityChange);
    resize();
    start();

    return () => {
      stop();
      resizeObserver.disconnect();
      intersectionObserver.disconnect();
      canvas.removeEventListener("pointermove", onPointerMove);
      canvas.removeEventListener("pointerleave", onPointerLeave);
      document.removeEventListener("visibilitychange", onVisibilityChange);
      geometry.dispose();
      material.dispose();
      renderer.dispose();
      renderer.forceContextLoss();
      canvas.remove();
    };
  }, [
    amplitude, brightness, crestColor, detail, fogDepth, grain, grainIntensity, height,
    horizonColor, mouseInteraction, opacity, parallaxStrength, speed, swell, tilt,
    turbulence, waveColor, waveRatio, waveScale, zoom,
  ]);

  return <div ref={containerRef} className={`gradient-waves-container ${className}`.trim()} />;
}

export default memo(GradientWaves);
