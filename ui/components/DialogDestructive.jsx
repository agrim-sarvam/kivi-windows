import { Button } from './Button.jsx';

// figma node: 1:4433 dialog / destructive
export function DialogDestructive(_p = {}) {
  const props = _p;
  return (
    <div className={props.className} style={{
      width: 380,
      borderRadius: 20,
      backgroundColor: "rgb(255,255,255)",
      boxShadow: "0px 0px 64px 0px rgba(20,20,20,0.16)",
      display: "flex",
      flexDirection: "column",
      gap: 6,
      padding: "24px 26px 24px 26px",
      alignItems: "flex-start",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <span style={{
        position: "relative",
        fontFamily: "\"Space Grotesk\", -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 500,
        fontSize: 18,
        whiteSpace: "nowrap",
        lineHeight: 1.2999999523162842,
        letterSpacing: "-0.010em",
        color: "rgb(20,24,14)",
        flexShrink: 0,
      }}>{props.text1 ?? "delete \"work messaging\"?"}</span>
      <span style={{
        position: "relative",
        fontFamily: "Inter, -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif",
        fontWeight: 400,
        fontSize: 13.5,
        lineHeight: 1.600000023841858,
        color: "rgb(92,100,84)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text2 ?? "The persona and its 3 custom rules will be removed. Apps assigned to it fall back to casual. This can’t be undone."}</span>
      <div style={{
        position: "relative",
        overflow: "hidden",
        display: "flex",
        flexDirection: "row",
        gap: 10,
        padding: "12px 0px 12px 0px",
        justifyContent: "flex-end",
        alignItems: "flex-start",
        flexWrap: "nowrap",
        boxSizing: "border-box",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>
        <div style={{ position: "relative", flexShrink: 0 }}>{props.icon1 ?? <Button kind={"ghost"} size={"md"} />}</div>
        <Button
          style={{ position: "relative", flexShrink: 0 }}
          kind={"danger"}
          size={"md"}
        />
      </div>
    </div>
  );
}
export default DialogDestructive;
