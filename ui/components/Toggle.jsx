// figma node: 1:4400 toggle (2 variants)
const __venc = (v) => String(v).replace(/[%|=]/g, encodeURIComponent);
const __vkey = (p) => "state=" + __venc(p.state);

export function Toggle(_p = {}) {
  const props = { ..._p, state: _p.state ?? "on" };
  const __body0 = () => (
    <div className={props.className} style={{
      width: 40,
      height: 23,
      borderRadius: 9999,
      backgroundColor: "rgb(65,105,30)",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "absolute",
        left: 19.5,
        top: 2.5,
        width: 18,
        height: 18,
        borderRadius: "50%",
        backgroundColor: "rgb(255,255,255)",
        boxShadow: "0px 1px 3px 0px rgba(0,0,0,0.2)",
      }} />
    </div>
  );
  const __body1 = () => (
    <div className={props.className} style={{
      width: 40,
      height: 23,
      borderRadius: 9999,
      backgroundColor: "rgb(225,230,216)",
      position: "relative",
      ...props.style,
    }}>
      <div style={{
        position: "absolute",
        left: 2.5,
        top: 2.5,
        width: 18,
        height: 18,
        borderRadius: "50%",
        backgroundColor: "rgb(255,255,255)",
        boxShadow: "0px 1px 3px 0px rgba(0,0,0,0.2)",
      }} />
    </div>
  );
  const __impls = {
    // figma: state=on
    "state=on": __body0,
    // figma: state=off
    "state=off": __body1,
  };
  return (__impls[__vkey(props)] ?? __body0)();
}
export default Toggle;
