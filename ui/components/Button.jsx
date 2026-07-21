// figma node: 1:4374 button (8 variants)
const __venc = (v) => String(v).replace(/[%|=]/g, encodeURIComponent);
const __vkey = (p) => "kind=" + __venc(p.kind) + '|' + "size=" + __venc(p.size);

export function Button(_p = {}) {
  const props = { ..._p, kind: _p.kind ?? "primary", size: _p.size ?? "md" };
  const __body0 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(22,30,16)",
      display: "flex",
      flexDirection: "row",
      gap: 8,
      padding: "9px 18px 9px 18px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 500,
        fontSize: 14,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(241,244,236)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "dictate"}</span>
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 11,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(169,215,126)",
        flexShrink: 0,
      }}>{props.text2 ?? "R Ctrl"}</span>
    </div>
  );
  const __body1 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(22,30,16)",
      display: "flex",
      flexDirection: "row",
      gap: 8,
      padding: "6px 13px 6px 13px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 500,
        fontSize: 12,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(241,244,236)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "dictate"}</span>
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 11,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(169,215,126)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text2 ?? "R Ctrl"}</span>
    </div>
  );
  const __body2 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(255,255,255)",
      boxShadow: "inset 0 0 0 1px rgb(225,230,216)",
      display: "flex",
      flexDirection: "row",
      gap: 8,
      padding: "9px 18px 9px 18px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 500,
        fontSize: 14,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(20,24,14)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "edit"}</span>
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 11,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(65,105,30)",
        flexShrink: 0,
      }}>{props.text2 ?? "R Ctrl"}</span>
    </div>
  );
  const __body3 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(255,255,255)",
      boxShadow: "inset 0 0 0 1px rgb(225,230,216)",
      display: "flex",
      flexDirection: "row",
      gap: 8,
      padding: "6px 13px 6px 13px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 500,
        fontSize: 12,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(20,24,14)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "edit"}</span>
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 11,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(65,105,30)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text2 ?? "R Ctrl"}</span>
    </div>
  );
  const __body4 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      display: "flex",
      flexDirection: "row",
      gap: 8,
      padding: "9px 18px 9px 18px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 500,
        fontSize: 14,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(92,100,84)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "clear"}</span>
    </div>
  );
  const __body5 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      display: "flex",
      flexDirection: "row",
      gap: 8,
      padding: "6px 13px 6px 13px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 500,
        fontSize: 12,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(92,100,84)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "clear"}</span>
    </div>
  );
  const __body6 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(253,231,226)",
      display: "flex",
      flexDirection: "row",
      gap: 8,
      padding: "9px 18px 9px 18px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 500,
        fontSize: 14,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(184,21,20)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "Delete persona"}</span>
    </div>
  );
  const __body7 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(253,231,226)",
      display: "flex",
      flexDirection: "row",
      gap: 8,
      padding: "6px 13px 6px 13px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 500,
        fontSize: 12,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(184,21,20)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "Delete persona"}</span>
    </div>
  );
  const __impls = {
    // figma: kind=primary, size=md
    "kind=primary|size=md": __body0,
    // figma: kind=primary, size=sm
    "kind=primary|size=sm": __body1,
    // figma: kind=secondary, size=md
    "kind=secondary|size=md": __body2,
    // figma: kind=secondary, size=sm
    "kind=secondary|size=sm": __body3,
    // figma: kind=ghost, size=md
    "kind=ghost|size=md": __body4,
    // figma: kind=ghost, size=sm
    "kind=ghost|size=sm": __body5,
    // figma: kind=danger, size=md
    "kind=danger|size=md": __body6,
    // figma: kind=danger, size=sm
    "kind=danger|size=sm": __body7,
  };
  return (__impls[__vkey(props)] ?? __body0)();
}
export default Button;
