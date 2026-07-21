import * as React from 'react';
export interface StatTileProps {
  className?: string;
  style?: React.CSSProperties;
  /** Text content; defaults to "WORDS / MIN". */
  text1?: string;
  /** Text content; defaults to "162". */
  text2?: string;
  /** Text content; defaults to "vs 42 typing". */
  text3?: string;
}
export declare const StatTile: React.FC<StatTileProps>;
export default StatTile;
