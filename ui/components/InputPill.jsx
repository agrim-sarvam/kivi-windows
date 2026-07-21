// figma node: 1:4401 input / pill
export function InputPill(_p = {}) {
  const props = _p;
  return (
    <div className={props.className} style={{
      width: 340,
      borderRadius: 9999,
      backgroundColor: "rgb(255,255,255)",
      boxShadow: "inset 0 0 0 1px rgb(225,230,216)",
      display: "flex",
      flexDirection: "row",
      gap: 10,
      padding: "10px 18px 10px 18px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 400,
        fontSize: 14,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        color: "rgb(146,154,138)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "Search transcripts"}</span>
    </div>
  );
}
export default InputPill;
