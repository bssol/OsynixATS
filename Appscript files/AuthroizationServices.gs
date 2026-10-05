/**
 * Osynix ATS - AuthorizationService.gs
 *
 * Central server-side authorization helpers.
 *
 * Purpose:
 * - Validate every protected ATS server request against the live session.
 * - Re-check live user status through validateATSSession().
 * - Enforce Admin / Recruiter role access.
 * - Enforce explicit permissions where required.
 *
 * IMPORTANT:
 * - This file does NOT replace AuthService.gs.
 * - It depends on validateATSSession(sessionToken) from AuthService.gs.
 * - Admin is treated as full-access for operational permissions.
 */


/* =========================================================
   SESSION
   ========================================================= */

function requireValidATSSession_(sessionToken) {

  const token =
    String(
      sessionToken || ''
    ).trim();


  if (!token) {
    throw new Error(
      'Your ATS session is missing. Please sign in again.'
    );
  }


  const result =
    validateATSSession(
      token
    );


  if (
    !result ||
    result.valid !== true ||
    !result.user
  ) {
    throw new Error(
      'Your ATS session is no longer valid. Please sign in again.'
    );
  }


  return result.user;
}


/* =========================================================
   ROLE
   ========================================================= */

function requireATSRole_(
  sessionToken,
  allowedRoles
) {

  const user =
    requireValidATSSession_(
      sessionToken
    );


  const allowed =
    Array.isArray(
      allowedRoles
    )
      ? allowedRoles
      : [
          allowedRoles
        ];


  const normalizedAllowed =
    allowed
      .filter(Boolean)
      .map(
        function(role) {
          return normalizeATSRole_(
            role
          );
        }
      );


  const userRole =
    normalizeATSRole_(
      user.role
    );


  if (
    !userRole ||
    normalizedAllowed.indexOf(
      userRole
    ) === -1
  ) {
    throw new Error(
      'You do not have permission to perform this ATS action.'
    );
  }


  return user;
}


/* =========================================================
   STANDARD OPERATIONAL ACCESS
   ========================================================= */

function requireATSOperationalUser_(
  sessionToken
) {

  return requireATSRole_(
    sessionToken,
    [
      'Admin',
      'Recruiter'
    ]
  );
}


/* =========================================================
   PERMISSION
   ========================================================= */

function requireATSPermission_(
  sessionToken,
  permissionName
) {

  const user =
    requireATSOperationalUser_(
      sessionToken
    );


  const role =
    normalizeATSRole_(
      user.role
    );


  // Admin has full operational access.
  if (
    role === 'admin'
  ) {
    return user;
  }


  const permissions =
    user.permissions &&
    typeof user.permissions ===
      'object'
      ? user.permissions
      : {};


  if (
    permissions[
      permissionName
    ] !== true
  ) {
    throw new Error(
      'You do not have permission to perform this ATS action.'
    );
  }


  return user;
}


/* =========================================================
   SPECIFIC PERMISSION HELPERS
   ========================================================= */

function requireCanCreatePositions_(
  sessionToken
) {

  return requireATSPermission_(
    sessionToken,
    'canCreatePositions'
  );
}


function requireCanViewTalentPool_(
  sessionToken
) {

  return requireATSPermission_(
    sessionToken,
    'canViewTalentPool'
  );
}


function requireCanExportReports_(
  sessionToken
) {

  return requireATSPermission_(
    sessionToken,
    'canExportReports'
  );
}


/* =========================================================
   ROLE HELPERS
   ========================================================= */

function normalizeATSRole_(
  value
) {

  return String(
    value || ''
  )
    .trim()
    .toLowerCase();
}


function isATSAdmin_(
  user
) {

  return (
    normalizeATSRole_(
      user &&
      user.role
    ) ===
    'admin'
  );
}


function isATSRecruiter_(
  user
) {

  return (
    normalizeATSRole_(
      user &&
      user.role
    ) ===
    'recruiter'
  );
}
