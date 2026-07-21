// figma node: 1:4411 history row
export function HistoryRow(_p = {}) {
  const props = _p;
  return (
    <div className={props.className} style={{
      width: 640,
      backgroundColor: "rgb(255,255,255)",
      display: "flex",
      flexDirection: "row",
      gap: 14,
      padding: "12px 16px 12px 16px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "relative",
        width: 26,
        overflow: "hidden",
        borderRadius: 7,
        backgroundColor: "rgb(231,238,221)",
        display: "flex",
        flexDirection: "row",
        justifyContent: "center",
        alignItems: "center",
        flexWrap: "nowrap",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>
        <span style={{
          position: "relative",
          fontFamily: "\"Space Grotesk\", -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
          fontWeight: 500,
          fontSize: 12,
          whiteSpace: "nowrap",
          lineHeight: 1,
          color: "rgb(92,100,84)",
          flexShrink: 0,
        }}>{props.text1 ?? "S"}</span>
      </div>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 400,
        fontSize: 14,
        lineHeight: 1.2000000476837158,
        color: "rgb(20,24,14)",
        flexGrow: 1,
      }}>{props.text2 ?? "Kal 3 PM works — see you then. I’ll send the doc to Priyank before that."}</span>
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 12,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(92,100,84)",
        flexShrink: 0,
      }}>{props.text3 ?? "hi-IN"}</span>
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 12,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(92,100,84)",
        flexShrink: 0,
      }}>{props.text4 ?? "2m"}</span>
    </div>
  );
}
export default HistoryRow;
