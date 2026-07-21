// figma node: 1:4394 state chip (6 variants)
const __venc = (v) => String(v).replace(/[%|=]/g, encodeURIComponent);
const __vkey = (p) => "state=" + __venc(p.state);

export function StateChip(_p = {}) {
  const props = { ..._p, state: _p.state ?? "idle" };
  const __body0 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(241,244,236)",
      boxShadow: "inset 0 0 0 1px rgb(225,230,216)",
      display: "flex",
      flexDirection: "row",
      gap: 7,
      padding: "5px 12px 5px 12px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "relative",
        width: 7,
        height: 7,
        borderRadius: "50%",
        backgroundColor: "rgb(146,154,138)",
        flexShrink: 0,
      }} />
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 11.5,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        letterSpacing: "0.040em",
        color: "rgb(92,100,84)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "idle"}</span>
    </div>
  );
  const __body1 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(254,237,230)",
      display: "flex",
      flexDirection: "row",
      gap: 7,
      padding: "5px 12px 5px 12px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "relative",
        width: 7,
        height: 7,
        borderRadius: "50%",
        backgroundColor: "rgb(233,108,47)",
        flexShrink: 0,
      }} />
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 11.5,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        letterSpacing: "0.040em",
        color: "rgb(233,108,47)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "listening"}</span>
    </div>
  );
  const __body2 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(232,239,252)",
      display: "flex",
      flexDirection: "row",
      gap: 7,
      padding: "5px 12px 5px 12px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "relative",
        width: 7,
        height: 7,
        borderRadius: "50%",
        backgroundColor: "rgb(66,80,213)",
        flexShrink: 0,
      }} />
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 11.5,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        letterSpacing: "0.040em",
        color: "rgb(66,80,213)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "processing"}</span>
    </div>
  );
  const __body3 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(227,241,216)",
      display: "flex",
      flexDirection: "row",
      gap: 7,
      padding: "5px 12px 5px 12px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "relative",
        width: 7,
        height: 7,
        borderRadius: "50%",
        backgroundColor: "rgb(75,125,40)",
        flexShrink: 0,
      }} />
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 11.5,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        letterSpacing: "0.040em",
        color: "rgb(75,125,40)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "speaking"}</span>
    </div>
  );
  const __body4 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(255,242,210)",
      display: "flex",
      flexDirection: "row",
      gap: 7,
      padding: "5px 12px 5px 12px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "relative",
        width: 7,
        height: 7,
        borderRadius: "50%",
        backgroundColor: "rgb(210,150,45)",
        flexShrink: 0,
      }} />
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 11.5,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        letterSpacing: "0.040em",
        color: "rgb(210,150,45)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "waiting"}</span>
    </div>
  );
  const __body5 = () => (
    <div className={props.className} style={{
      width: "fit-content",
      borderRadius: 9999,
      backgroundColor: "rgb(250,215,205)",
      display: "flex",
      flexDirection: "row",
      gap: 7,
      padding: "5px 12px 5px 12px",
      alignItems: "center",
      flexWrap: "nowrap",
      boxSizing: "border-box",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "relative",
        width: 7,
        height: 7,
        borderRadius: "50%",
        backgroundColor: "rgb(184,21,20)",
        flexShrink: 0,
      }} />
      <span style={{
        position: "relative",
        fontFamily: "\"Space Mono\", ui-monospace, \"SF Mono\", Menlo, Consolas, monospace",
        fontWeight: 400,
        fontSize: 11.5,
        whiteSpace: "nowrap",
        lineHeight: 1.2000000476837158,
        letterSpacing: "0.040em",
        color: "rgb(184,21,20)",
        flexShrink: 0,
        alignSelf: "stretch",
      }}>{props.text1 ?? "error"}</span>
    </div>
  );
  const __impls = {
    // figma: state=idle
    "state=idle": __body0,
    // figma: state=listening
    "state=listening": __body1,
    // figma: state=processing
    "state=processing": __body2,
    // figma: state=speaking
    "state=speaking": __body3,
    // figma: state=waiting
    "state=waiting": __body4,
    // figma: state=error
    "state=error": __body5,
  };
  return (__impls[__vkey(props)] ?? __body0)();
}
export default StateChip;
