// figma node: 1:4418 transcript box / listening
export function TranscriptBoxListening(_p = {}) {
  const props = _p;
  return (
    <div className={props.className} style={{
      width: 560,
      borderRadius: 20,
      backgroundColor: "rgb(255,255,255)",
      boxShadow: "inset 0 0 0 1px rgb(237,240,230), 0px 0px 64px 0px rgba(20,20,20,0.16)",
      display: "flex",
      flexDirection: "column",
      gap: 10,
      padding: "16px 20px 12px 20px",
      alignItems: "flex-start",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "relative",
        overflow: "hidden",
        display: "flex",
        flexDirection: "row",
        justifyContent: "space-between",
        alignItems: "flex-start",
        flexWrap: "nowrap",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>
        <span style={{
          position: "relative",
          fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
          fontWeight: 400,
          fontSize: 11,
          whiteSpace: "nowrap",
          lineHeight: 1.2000000476837158,
          letterSpacing: "0.080em",
          color: "rgb(146,154,138)",
          textTransform: "uppercase",
          flexShrink: 0,
        }}>{props.text1 ?? "LIVE"}</span>
        <span style={{
          position: "relative",
          fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
          fontWeight: 400,
          fontSize: 12,
          whiteSpace: "nowrap",
          lineHeight: 1.2000000476837158,
          color: "rgb(92,100,84)",
          flexShrink: 0,
        }}>{props.text2 ?? "hi-IN · auto"}</span>
      </div>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 400,
        fontSize: 15,
        lineHeight: 1.649999976158142,
        color: "rgb(146,154,138)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text3 ?? "Press right ctrl and speak — finished text appears here, in your style…"}</span>
      <div style={{
        position: "relative",
        overflow: "hidden",
        display: "flex",
        flexDirection: "row",
        padding: "10px 0px 10px 0px",
        justifyContent: "space-between",
        alignItems: "flex-start",
        flexWrap: "nowrap",
        boxSizing: "border-box",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>
        <span style={{
          position: "relative",
          fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
          fontWeight: 400,
          fontSize: 12,
          whiteSpace: "nowrap",
          lineHeight: 1.2000000476837158,
          color: "rgb(92,100,84)",
          flexShrink: 0,
        }}>{props.text4 ?? "right ctrl to stop · esc to discard"}</span>
      </div>
    </div>
  );
}
export default TranscriptBoxListening;
