import type { BusinessDetails } from "./types";

export function BusinessDetailsView({
  details,
  compact = false,
}: {
  details: BusinessDetails;
  compact?: boolean;
}) {
  return (
    <>
      <div className="font-semibold">{details.name}</div>
      {compact ? (
        <div>
          ИНН: {details.taxId} · ОГРНИП: {details.registrationId}
        </div>
      ) : (
        <>
          <div>ИНН: {details.taxId}</div>
          <div>ОГРНИП: {details.registrationId}</div>
        </>
      )}
      {compact ? (
        <div>{details.addressLines.join(" ")}</div>
      ) : (
        details.addressLines.map((line) => <div key={line}>{line}</div>)
      )}
      <div>{details.taxOffice}</div>
      <div className="pt-1">
        <a href={`mailto:${details.email}`} className="hover:underline">
          {details.email}
        </a>
        {compact ? ` · ${details.hours}` : null}
      </div>
      {!compact ? <div>{details.hours}</div> : null}
    </>
  );
}
