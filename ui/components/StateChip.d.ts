import * as React from 'react';
export interface StateChipProps {
  className?: string;
  style?: React.CSSProperties;
  state?: "idle" | "listening" | "processing" | "speaking" | "waiting" | "error";
  /** Text content; defaults to "idle". */
  text1?: string;
}
export declare const StateChip: React.FC<StateChipProps>;
export default StateChip;
