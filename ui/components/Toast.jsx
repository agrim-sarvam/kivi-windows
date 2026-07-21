// figma node: 1:4403 toast
export function Toast(_p = {}) {
  const props = _p;
  return (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(255,255,255)",
      boxShadow: "inset 0 0 0 1px rgb(237,240,230), 0px 0px 64px 0px rgba(20,20,20,0.16)",
      display: "flex",
      flexDirection: "row",
      gap: 10,
      padding: "10px 16px 10px 16px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "relative",
        width: 8,
        height: 8,
        borderRadius: "50%",
        backgroundColor: "rgb(110,163,53)",
        flexShrink: 0,
      }} />
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 400,
        fontSize: 13.5,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(20,24,14)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "Pasted at cursor · Slack"}</span>
    </div>
  );
}
export default Toast;
