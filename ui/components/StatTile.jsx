// figma node: 1:4407 stat tile
export function StatTile(_p = {}) {
  const props = _p;
  return (
    <div className={props.className} style={{
      width: 220,
      borderRadius: 20,
      backgroundColor: "rgb(255,255,255)",
      boxShadow: "inset 0 0 0 1px rgb(237,240,230), 0px 0px 64px 0px rgba(20,20,20,0.08)",
      display: "flex",
      flexDirection: "column",
      gap: 6,
      padding: "16px 20px 16px 20px",
      alignItems: "flex-start",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 10.5,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        letterSpacing: "0.100em",
        color: "rgb(146,154,138)",
        textTransform: "uppercase",
        flexShrink: 0,
      }}>{props.text1 ?? "WORDS / MIN"}</span>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 500,
        fontSize: 32,
        whiteSpace: "nowrap",
        lineHeight: 1.100000023841858,
        color: "rgb(20,24,14)",
        flexShrink: 0,
      }}>{props.text2 ?? "162"}</span>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 400,
        fontSize: 12,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(92,100,84)",
        flexShrink: 0,
      }}>{props.text3 ?? "vs 42 typing"}</span>
    </div>
  );
}
export default StatTile;
