declare module "cs2/api" {
  export interface ValueBinding<T> {
    value: T;
    subscribe(callback: (value: T) => void): () => void;
  }

  export function bindValue<T>(group: string, name: string, fallback: T): ValueBinding<T>;
  export function useValue<T>(binding: ValueBinding<T>): T;
  export function trigger(group: string, name: string, ...args: unknown[]): void;
}

declare module "cs2/modding" {
  export interface ModuleRegistry {
    append(target: string, component: React.ComponentType): void;
    extend(target: string, exportName: string, extender: (previous: React.ComponentType<any>) => React.ComponentType<any>): void;
    hasAppend?(target: string): boolean;
  }

  export type ModRegistrar = (registry: ModuleRegistry) => void;
}

declare module "*.module.scss" {
  const classes: Record<string, string>;
  export default classes;
}

declare module "cs2/ui" {
  export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
    variant?: "floating";
    selected?: boolean;
    onSelect?: () => void;
    tooltipLabel?: React.ReactNode;
  }
  export const Button: React.ComponentType<ButtonProps>;
}

declare module "mod.json" {
  const value: { id: string; author: string; version: string; dependencies: string[] };
  export default value;
}
