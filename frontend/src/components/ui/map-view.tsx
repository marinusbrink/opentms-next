import { forwardRef, useEffect, useImperativeHandle, useRef, useState, type ReactNode } from "react";
import MapGL, { AttributionControl, NavigationControl, type MapLayerMouseEvent, type MapRef } from "react-map-gl/maplibre";
import "maplibre-gl/dist/maplibre-gl.css";

import { cn } from "@/lib/utils";

/* The map (design 66 D15, design 70 "Map"): MapLibre GL through react-map-gl, on OpenFreeMap's "positron" vector style
 * with its OpenStreetMap attribution always visible (never collapsed). Sources, layers, markers and popups are its
 * children (re-exported here, so feature code imports the map from this one file). Load it through map-view-lazy.tsx:
 * MapLibre is large and only the Map pane needs it. Before go-live the basemap moves to self-hosted PMTiles (rollout);
 * then only MAP_STYLE_URL changes. Paint colours for layers come from the theme tokens via `tokenColor`. */

export { Layer, Marker, Popup, Source, useMap } from "react-map-gl/maplibre";
export type { LayerProps, MapLayerMouseEvent, MapRef, MarkerProps, PopupProps, SourceProps } from "react-map-gl/maplibre";

export const MAP_STYLE_URL = "https://tiles.openfreemap.org/styles/positron";

export interface MapPoint {
  longitude: number;
  latitude: number;
}

/** [[west, south], [east, north]]. */
export type MapBounds = [[number, number], [number, number]];

export interface MapViewState {
  longitude: number;
  latitude: number;
  zoom: number;
}

/** The Netherlands, as a first view before anything is fitted. */
export const DEFAULT_MAP_VIEW: MapViewState = { longitude: 5.3, latitude: 52.2, zoom: 6.6 };

/** The bounds around the points (invalid coordinates skipped), or null when none is left. */
export function boundsOf(points: readonly (MapPoint | null | undefined)[]): MapBounds | null {
  let west = Infinity;
  let south = Infinity;
  let east = -Infinity;
  let north = -Infinity;
  for (const p of points) {
    if (!p || !Number.isFinite(p.longitude) || !Number.isFinite(p.latitude)) continue;
    if (Math.abs(p.latitude) > 90 || Math.abs(p.longitude) > 180) continue;
    west = Math.min(west, p.longitude);
    east = Math.max(east, p.longitude);
    south = Math.min(south, p.latitude);
    north = Math.max(north, p.latitude);
  }
  return Number.isFinite(west) ? [[west, south], [east, north]] : null;
}

export interface FitBoundsOptions {
  /** Pixels kept free around the bounds; default 48. */
  padding?: number;
  /** Never zoom closer than this (a single point); default 14. */
  maxZoom?: number;
  /** Animation in ms; default 400, 0 jumps. */
  duration?: number;
}

/** Fits the map to points or bounds; does nothing without a map or without valid points. */
export function fitBounds(map: MapRef | null | undefined, target: MapBounds | readonly (MapPoint | null | undefined)[], options?: FitBoundsOptions): void {
  if (!map) return;
  const bounds = isBounds(target) ? target : boundsOf(target);
  if (!bounds) return;
  map.fitBounds(bounds, { padding: options?.padding ?? 48, maxZoom: options?.maxZoom ?? 14, duration: options?.duration ?? 400 });
}

function isBounds(value: unknown): value is MapBounds {
  return Array.isArray(value) && value.length === 2 && Array.isArray(value[0]) && typeof value[0][0] === "number";
}

/**
 * The computed value of a theme token (`--brand`, `--tone-danger-fg`, …) for MapLibre paint properties, which cannot
 * read CSS variables. The brand is set at runtime by the tenant theme, so read it when painting, not at module load.
 * MapLibre parses only hex, rgb(a), hsl(a) and named colours; tokens written in another syntax (the shadcn tokens are
 * oklch) are converted to rgba through a one-pixel canvas. Without a canvas (tests) the raw value is returned.
 */
export function tokenColor(token: `--${string}`, element: Element = document.documentElement): string {
  return toMapColor(getComputedStyle(element).getPropertyValue(token).trim());
}

const mapColors = new Map<string, string>();
let colorCanvas: CanvasRenderingContext2D | null | undefined;

/** A CSS colour as MapLibre can parse it (hex, rgb and hsl pass unchanged; anything else becomes `rgba(…)`). */
export function toMapColor(value: string): string {
  if (!value || /^(#|rgba?\(|hsla?\()/i.test(value)) return value;
  const cached = mapColors.get(value);
  if (cached) return cached;
  if (colorCanvas === undefined) {
    try {
      const canvas = document.createElement("canvas");
      canvas.width = 1;
      canvas.height = 1;
      colorCanvas = canvas.getContext("2d", { willReadFrequently: true });
    } catch {
      colorCanvas = null;
    }
  }
  if (!colorCanvas) return value;
  colorCanvas.clearRect(0, 0, 1, 1);
  colorCanvas.fillStyle = value;
  colorCanvas.fillRect(0, 0, 1, 1);
  const [r, g, b, a] = colorCanvas.getImageData(0, 0, 1, 1).data;
  const result = `rgba(${r}, ${g}, ${b}, ${Math.round((a / 255) * 1000) / 1000})`;
  mapColors.set(value, result);
  return result;
}

export interface MapViewProps {
  /** The first view; default the Netherlands. */
  initialView?: MapViewState;
  /** Fitted whenever it changes (e.g. the focused trip's stops). */
  bounds?: MapBounds | null;
  fitOptions?: FitBoundsOptions;
  /** Layers whose features raise `onClick` / `onMouseEnter` with `features`. */
  interactiveLayerIds?: string[];
  onClick?: (event: MapLayerMouseEvent) => void;
  onMouseEnter?: (event: MapLayerMouseEvent) => void;
  onMouseLeave?: (event: MapLayerMouseEvent) => void;
  /** Called once the style has loaded. */
  onLoad?: (map: MapRef) => void;
  /** The map's accessible name (already translated). */
  "aria-label"?: string;
  className?: string;
  children?: ReactNode;
}

export const MapView = forwardRef<MapRef | null, MapViewProps>(function MapView(
  { initialView = DEFAULT_MAP_VIEW, bounds, fitOptions, interactiveLayerIds, onClick, onMouseEnter, onMouseLeave, onLoad, className, children, ...aria },
  ref,
) {
  const mapRef = useRef<MapRef | null>(null);
  const [loaded, setLoaded] = useState(false);
  useImperativeHandle<MapRef | null, MapRef | null>(ref, () => mapRef.current);

  const fitRef = useRef(fitOptions);
  fitRef.current = fitOptions;
  /* The bounds are compared by value (boundsKey), not by array identity. */
  const boundsRef = useRef(bounds);
  boundsRef.current = bounds;
  const boundsKey = bounds ? bounds.flat().join(",") : "";
  useEffect(() => {
    if (!loaded || !boundsKey || !boundsRef.current) return;
    fitBounds(mapRef.current, boundsRef.current, fitRef.current);
  }, [loaded, boundsKey]);

  return (
    <div className={cn("relative h-full min-h-0 w-full", className)} role="region" aria-label={aria["aria-label"]}>
      <MapGL
        ref={mapRef}
        initialViewState={initialView}
        mapStyle={MAP_STYLE_URL}
        attributionControl={false}
        interactiveLayerIds={interactiveLayerIds}
        onClick={onClick}
        onMouseEnter={onMouseEnter}
        onMouseLeave={onMouseLeave}
        onLoad={() => {
          setLoaded(true);
          if (mapRef.current) onLoad?.(mapRef.current);
        }}
        style={{ width: "100%", height: "100%" }}
      >
        <AttributionControl compact={false} position="bottom-right" />
        <NavigationControl position="top-right" showCompass={false} />
        {children}
      </MapGL>
    </div>
  );
});
