import * as React from 'react';
export interface ToggleProps {
  className?: string;
  style?: React.CSSProperties;
  state?: "on" | "off";
}
export declare const Toggle: React.FC<ToggleProps>;
export default Toggle;
