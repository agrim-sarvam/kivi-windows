import * as React from 'react';
export interface InputPillProps {
  className?: string;
  style?: React.CSSProperties;
  /** Text content; defaults to "Search transcripts". */
  text1?: string;
}
export declare const InputPill: React.FC<InputPillProps>;
export default InputPill;
