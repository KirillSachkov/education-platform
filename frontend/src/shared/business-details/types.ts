/** Operator-supplied details intended for display in the public business footer. */
export interface BusinessDetails {
  name: string;
  taxId: string;
  registrationId: string;
  addressLines: string[];
  taxOffice: string;
  email: string;
  hours: string;
  copyrightName: string;
}
