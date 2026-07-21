import * as React from 'react';
export interface HistoryRowProps {
  className?: string;
  style?: React.CSSProperties;
  /** Text content; defaults to "S". */
  text1?: string;
  /** Text content; defaults to "Kal 3 PM works — see you then. I’ll send the doc to Priyank before that.". */
  text2?: string;
  /** Text content; defaults to "hi-IN". */
  text3?: string;
  /** Text content; defaults to "2m". */
  text4?: string;
}
export declare const HistoryRow: React.FC<HistoryRowProps>;
export default HistoryRow;
