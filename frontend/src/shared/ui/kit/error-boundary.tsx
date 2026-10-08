"use client";

import { AlertCircle, RefreshCw } from "lucide-react";
import { Component, type ReactNode } from "react";
import { Button } from "./button";
import { Card, CardContent, CardFooter, CardHeader } from "./card";

type ErrorBoundaryProps = {
  children: ReactNode;
  fallback?: ReactNode;
  onReset?: () => void;
};

type ErrorBoundaryState = {
  hasError: boolean;
  error: Error | null;
};

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  handleReset = () => {
    this.setState({ hasError: false, error: null });
    this.props.onReset?.();
  };

  override render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }

      return (
        <Card className="mx-auto max-w-md border-destructive/50 bg-destructive/5">
          <CardHeader className="flex flex-row items-center gap-3 pb-2">
            <AlertCircle className="h-6 w-6 text-destructive" />
            <h3 className="font-semibold text-lg">Что-то пошло не так</h3>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-muted-foreground">
              Произошла непредвиденная ошибка. Попробуйте обновить страницу или повторить действие.
            </p>
            {process.env.NODE_ENV === "development" && this.state.error && (
              <pre className="mt-3 p-3 bg-muted rounded-md text-xs overflow-auto max-h-32">
                {this.state.error.message}
              </pre>
            )}
          </CardContent>
          <CardFooter className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={this.handleReset}
              className="flex items-center gap-2"
            >
              <RefreshCw className="h-4 w-4" />
              Попробовать снова
            </Button>
            <Button variant="ghost" size="sm" onClick={() => window.location.reload()}>
              Обновить страницу
            </Button>
          </CardFooter>
        </Card>
      );
    }

    return this.props.children;
  }
}

// Функциональная обёртка для удобства использования с хуками
type WithErrorBoundaryProps = {
  children: ReactNode;
  fallback?: ReactNode;
  onReset?: () => void;
};

export function WithErrorBoundary({ children, fallback, onReset }: WithErrorBoundaryProps) {
  return (
    <ErrorBoundary fallback={fallback} onReset={onReset}>
      {children}
    </ErrorBoundary>
  );
}
