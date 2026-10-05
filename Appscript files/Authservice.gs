function requestLoginCode(
  email,
  deviceToken
) {
  email =
    String(email || '')
      .trim()
      .toLowerCase();

  deviceToken =
    String(deviceToken || '')
      .trim();


  if (!email) {
    throw new Error(
      'Please enter your email address.'
    );
  }


  const user =
    findATSUserByEmail_(email);


  if (!user) {
    return {
      success: false,
      status: 'NOT_AUTHORIZED',
      message:
        'This email is not authorized to access Osynix ATS.'
    };
  }


  if (
    String(user.status || '')
      .trim()
      .toLowerCase() !== 'active'
  ) {
    return {
      success: false,
      status:
        user.status || 'INACTIVE',
      message:
        'Your ATS access is not currently active.'
    };
  }


  /*
   * A trusted device can skip OTP.
   * Device trust now comes ONLY from the
   * Trusted Devices sheet.
   */
  if (
    deviceToken &&
    isTrustedATSDevice_(
      email,
      deviceToken
    )
  ) {
    return {
      success: true,
      status: 'TRUSTED_DEVICE',
      skipOtp: true,
      message:
        'Trusted device recognized.'
    };
  }


  const code =
    String(
      Math.floor(
        100000 +
        Math.random() * 900000
      )
    );


  const cache =
    CacheService.getScriptCache();


  cache.put(
    'ATS_OTP_' + email,
    code,
    600
  );


  MailApp.sendEmail({
    to: email,

    subject:
      'Your Osynix ATS verification code',

    htmlBody:
      '<div style="font-family:Arial,sans-serif;">' +
      '<h2 style="color:#1C1E66;">Osynix ATS</h2>' +
      '<p>Your verification code is:</p>' +
      '<div style="' +
      'font-size:28px;' +
      'font-weight:bold;' +
      'color:#175AFF;' +
      'letter-spacing:6px;' +
      'margin:20px 0;' +
      '">' +
      code +
      '</div>' +
      '<p>This code expires in 10 minutes.</p>' +
      '<p>If you did not request access, you can ignore this email.</p>' +
      '</div>'
  });


  return {
    success: true,
    status: 'CODE_SENT',
    skipOtp: false,
    message:
      'A verification code has been sent to your email.'
  };
}


function isTrustedATSDevice_(
  email,
  deviceToken
) {
  email =
    String(email || '')
      .trim()
      .toLowerCase();

  deviceToken =
    String(deviceToken || '')
      .trim();


  if (
    !email ||
    !deviceToken
  ) {
    return false;
  }


  const trustedDevice =
    findTrustedATSDevice_(
      email,
      deviceToken
    );


  return !!(
    trustedDevice &&
    String(
      trustedDevice.status || ''
    )
      .trim()
      .toLowerCase() === 'active'
  );
}


function verifyLoginCode(
  email,
  code,
  deviceToken,
  keepSignedIn
) {
  email =
    String(email || '')
      .trim()
      .toLowerCase();

  code =
    String(code || '')
      .trim();

  deviceToken =
    String(deviceToken || '')
      .trim();

  keepSignedIn =
    keepSignedIn === true ||
    String(keepSignedIn || '')
      .toLowerCase() === 'true';


  if (
    !email ||
    !code
  ) {
    throw new Error(
      'Email and verification code are required.'
    );
  }


  const cache =
    CacheService.getScriptCache();


  const savedCode =
    cache.get(
      'ATS_OTP_' + email
    );


  if (!savedCode) {
    return {
      success: false,
      status: 'CODE_EXPIRED',
      message:
        'The verification code has expired. Please request a new code.'
    };
  }


  if (
    savedCode !== code
  ) {
    return {
      success: false,
      status: 'INVALID_CODE',
      message:
        'The verification code is incorrect.'
    };
  }


  const user =
    findATSUserByEmail_(email);


  if (!user) {
    return {
      success: false,
      status: 'NOT_AUTHORIZED',
      message:
        'This email is not authorized.'
    };
  }


  if (
    String(user.status || '')
      .trim()
      .toLowerCase() !== 'active'
  ) {
    return {
      success: false,
      status: 'INACTIVE',
      message:
        'Your account is not active.'
    };
  }


  cache.remove(
    'ATS_OTP_' + email
  );


  if (!deviceToken) {
    deviceToken =
      Utilities.getUuid() +
      Utilities.getUuid();
  }


  /*
   * Persistent device trust is separate
   * from the temporary login session.
   */
  if (keepSignedIn) {
    trustATSDevice_(
      user,
      deviceToken
    );
  }


  const sessionToken =
    Utilities.getUuid() +
    Utilities.getUuid();


  const now =
    new Date();


  /*
   * All login sessions are temporary.
   * Trusted devices can create another
   * session later without another OTP.
   */
  const expiresAt =
    new Date(
      now.getTime() +
      12 *
      60 *
      60 *
      1000
    );


  const sessionData = {
    userId:
      user.userId,

    fullName:
      user.fullName,

    email:
      user.email,

    role:
      user.role,

    assignedClients:
      user.assignedClients,

    assignedRefs:
      user.assignedRefs,

    permissions:
      user.permissions
  };


  createATSSession_({
    sessionToken:
      sessionToken,

    userId:
      user.userId,

    email:
      user.email,

    role:
      user.role,

    deviceToken:
      deviceToken,

    keepSignedIn:
      keepSignedIn,

    createdAt:
      now,

    expiresAt:
      expiresAt
  });


  updateLastLogin_(
    email
  );


  logATSActivity_({
    email: email,
    fullName:
      user.fullName,
    role:
      user.role,
    action:
      'LOGIN',
    entityType:
      'User',
    entityId:
      user.userId,
    details:
      keepSignedIn
        ? 'Successful ATS login - device trusted'
        : 'Successful ATS login'
  });


  return {
    success: true,

    sessionToken:
      sessionToken,

    deviceToken:
      deviceToken,

    keepSignedIn:
      keepSignedIn,

    user:
      sessionData
  };
}


function validateATSSession(
  sessionToken
) {
  sessionToken =
    String(
      sessionToken || ''
    ).trim();


  if (!sessionToken) {
    return {
      valid: false
    };
  }


  const session =
    findATSSession_(
      sessionToken
    );


  if (!session) {
    return {
      valid: false
    };
  }


  if (
    String(
      session.status || ''
    )
      .trim()
      .toLowerCase() !== 'active'
  ) {
    return {
      valid: false
    };
  }


  const expiresAt =
    new Date(
      session.expiresAt
    );


  if (
    expiresAt &&
    !isNaN(expiresAt.getTime()) &&
    expiresAt.getTime() <
      new Date().getTime()
  ) {
    expireATSSession_(
      session.row
    );

    return {
      valid: false
    };
  }


  const user =
    findATSUserByEmail_(
      session.email
    );


  if (!user) {
    revokeATSSession_(
      session.row,
      'SYSTEM'
    );

    return {
      valid: false
    };
  }


  if (
    String(
      user.status || ''
    )
      .trim()
      .toLowerCase() !== 'active'
  ) {
    revokeATSSession_(
      session.row,
      'SYSTEM'
    );

    return {
      valid: false
    };
  }


  updateATSSessionActivity_(
    session.row
  );


  return {
    valid: true,

    user: {
      userId:
        user.userId,

      fullName:
        user.fullName,

      email:
        user.email,

      role:
        user.role,

      assignedClients:
        user.assignedClients,

      assignedRefs:
        user.assignedRefs,

      permissions:
        user.permissions
    }
  };
}
function createATSSession_(
  data
) {
  const ss =
    SpreadsheetApp.openById(
      CONFIG.SPREADSHEET_ID
    );

  const sheet =
    ss.getSheetByName(
      'User Sessions'
    );


  if (!sheet) {
    throw new Error(
      'User Sessions sheet not found.'
    );
  }


  const sessionHash =
    hashATSSessionToken_(
      data.sessionToken
    );


  const deviceTokenHash =
    data.deviceToken
      ? hashATSDeviceToken_(
          data.deviceToken
        )
      : '';


  sheet.appendRow([
    sessionHash,
    data.userId,
    data.email,
    data.role,
    deviceTokenHash,
    data.keepSignedIn
      ? 'Yes'
      : 'No',
    data.createdAt,
    data.createdAt,
    data.expiresAt,
    'Active',
    '',
    ''
  ]);
}


function findATSSession_(
  sessionToken
) {
  const ss =
    SpreadsheetApp.openById(
      CONFIG.SPREADSHEET_ID
    );

  const sheet =
    ss.getSheetByName(
      'User Sessions'
    );


  if (
    !sheet ||
    sheet.getLastRow() < 2
  ) {
    return null;
  }


  const sessionHash =
    hashATSSessionToken_(
      sessionToken
    );


  const values =
    sheet.getRange(
      2,
      1,
      sheet.getLastRow() - 1,
      12
    ).getValues();


  for (
    let i = 0;
    i < values.length;
    i++
  ) {
    if (
      String(values[i][0]) ===
      sessionHash
    ) {
      return {
        row:
          i + 2,

        userId:
          values[i][1],

        email:
          values[i][2],

        role:
          values[i][3],

        deviceToken:
          values[i][4],

        keepSignedIn:
          values[i][5],

        createdAt:
          values[i][6],

        lastActivity:
          values[i][7],

        expiresAt:
          values[i][8],

        status:
          values[i][9]
      };
    }
  }


  return null;
}


function updateATSSessionActivity_(
  row
) {
  const ss =
    SpreadsheetApp.openById(
      CONFIG.SPREADSHEET_ID
    );

  const sheet =
    ss.getSheetByName(
      'User Sessions'
    );


  sheet
    .getRange(
      row,
      8
    )
    .setValue(
      new Date()
    );
}


function expireATSSession_(
  row
) {
  const ss =
    SpreadsheetApp.openById(
      CONFIG.SPREADSHEET_ID
    );

  const sheet =
    ss.getSheetByName(
      'User Sessions'
    );


  sheet
    .getRange(
      row,
      10
    )
    .setValue(
      'Expired'
    );
}


function revokeATSSession_(
  row,
  revokedBy
) {
  const ss =
    SpreadsheetApp.openById(
      CONFIG.SPREADSHEET_ID
    );

  const sheet =
    ss.getSheetByName(
      'User Sessions'
    );


  sheet
    .getRange(
      row,
      10,
      1,
      3
    )
    .setValues([
      [
        'Revoked',
        new Date(),
        revokedBy || 'SYSTEM'
      ]
    ]);
}


function hashATSSessionToken_(
  token
) {
  const bytes =
    Utilities.computeDigest(
      Utilities.DigestAlgorithm.SHA_256,
      String(token || ''),
      Utilities.Charset.UTF_8
    );


  return bytes
    .map(
      function(byte) {
        const value =
          byte < 0
            ? byte + 256
            : byte;

        return (
          '0' +
          value.toString(16)
        ).slice(-2);
      }
    )
    .join('');
}

function logoutATS(
  sessionToken
) {
  sessionToken =
    String(
      sessionToken || ''
    ).trim();


  if (!sessionToken) {
    return {
      success: true
    };
  }


  const session =
    findATSSession_(
      sessionToken
    );


 if (session) {
  revokeATSSession_(
    session.row,
    'USER_LOGOUT'
  );
}


return {
  success: true
};
}


function loginWithTrustedDevice(
  email,
  deviceToken
) {
  email =
    String(
      email || ''
    )
      .trim()
      .toLowerCase();

  deviceToken =
    String(
      deviceToken || ''
    ).trim();


  if (
    !email ||
    !deviceToken
  ) {
    return {
      success: false,
      status: 'TRUST_REQUIRED',
      message:
        'Trusted device information is missing.'
    };
  }


  const user =
    findATSUserByEmail_(
      email
    );


  if (!user) {
    return {
      success: false,
      status: 'NOT_AUTHORIZED',
      message:
        'This email is not authorized.'
    };
  }


  if (
    String(
      user.status || ''
    )
      .trim()
      .toLowerCase() !== 'active'
  ) {
    return {
      success: false,
      status: 'INACTIVE',
      message:
        'Your ATS access is not currently active.'
    };
  }


  const trustedDevice =
    findTrustedATSDevice_(
      email,
      deviceToken
    );


  if (
    !trustedDevice ||
    String(
      trustedDevice.status || ''
    )
      .trim()
      .toLowerCase() !== 'active'
  ) {
    return {
      success: false,
      status: 'TRUST_REQUIRED',
      message:
        'This device is not currently trusted.'
    };
  }


  updateTrustedATSDeviceLastUsed_(
    trustedDevice.row
  );


  const sessionToken =
    Utilities.getUuid() +
    Utilities.getUuid();


  const now =
    new Date();


  const expiresAt =
    new Date(
      now.getTime() +
      12 *
      60 *
      60 *
      1000
    );


  createATSSession_({
    sessionToken:
      sessionToken,

    userId:
      user.userId,

    email:
      user.email,

    role:
      user.role,

    deviceToken:
      deviceToken,

    keepSignedIn:
      true,

    createdAt:
      now,

    expiresAt:
      expiresAt
  });


  const sessionData = {
    userId:
      user.userId,

    fullName:
      user.fullName,

    email:
      user.email,

    role:
      user.role,

    assignedClients:
      user.assignedClients,

    assignedRefs:
      user.assignedRefs,

    permissions:
      user.permissions
  };


  updateLastLogin_(
    user.email
  );


  logATSActivity_({
    email:
      user.email,

    fullName:
      user.fullName,

    role:
      user.role,

    action:
      'TRUSTED_DEVICE_LOGIN',

    entityType:
      'User',

    entityId:
      user.userId,

    details:
      'ATS login using trusted device'
  });


  return {
    success: true,

    sessionToken:
      sessionToken,

    deviceToken:
      deviceToken,

    keepSignedIn:
      true,

    user:
      sessionData
  };
}


function findTrustedATSDevice_(
  email,
  deviceToken
) {
  email =
    String(email || '')
      .trim()
      .toLowerCase();

  deviceToken =
    String(deviceToken || '')
      .trim();


  if (
    !email ||
    !deviceToken
  ) {
    return null;
  }


  const ss =
    SpreadsheetApp.openById(
      CONFIG.SPREADSHEET_ID
    );

  const sheet =
    ss.getSheetByName(
      'Trusted Devices'
    );


  if (
    !sheet ||
    sheet.getLastRow() < 2
  ) {
    return null;
  }


  const deviceHash =
    hashATSDeviceToken_(
      deviceToken
    );


  const values =
    sheet.getRange(
      2,
      1,
      sheet.getLastRow() - 1,
      10
    ).getValues();


  for (
    let i = values.length - 1;
    i >= 0;
    i--
  ) {

    const rowEmail =
      String(
        values[i][2] || ''
      )
        .trim()
        .toLowerCase();

    const rowDeviceHash =
      String(
        values[i][3] || ''
      ).trim();


    if (
      rowEmail === email &&
      rowDeviceHash === deviceHash
    ) {
      return {
        row:
          i + 2,

        trustedDeviceId:
          values[i][0],

        userId:
          values[i][1],

        email:
          values[i][2],

        deviceTokenHash:
          values[i][3],

        deviceLabel:
          values[i][4],

        trustedAt:
          values[i][5],

        lastUsedAt:
          values[i][6],

        status:
          values[i][7],

        revokedAt:
          values[i][8],

        revokedBy:
          values[i][9]
      };
    }
  }


  return null;
}


function trustATSDevice_(
  user,
  deviceToken
) {
  if (
    !user ||
    !user.email ||
    !deviceToken
  ) {
    return null;
  }


  const ss =
    SpreadsheetApp.openById(
      CONFIG.SPREADSHEET_ID
    );

  const sheet =
    ss.getSheetByName(
      'Trusted Devices'
    );


  if (!sheet) {
    throw new Error(
      'Trusted Devices sheet not found.'
    );
  }


  const now =
    new Date();


  const existing =
    findTrustedATSDevice_(
      user.email,
      deviceToken
    );


  if (existing) {

    sheet
      .getRange(
        existing.row,
        2,
        1,
        9
      )
      .setValues([[
        user.userId,
        user.email,
        hashATSDeviceToken_(
          deviceToken
        ),
        existing.deviceLabel ||
          'Trusted browser',
        now,
        now,
        'Active',
        '',
        ''
      ]]);


    return {
      row:
        existing.row,

      trustedDeviceId:
        existing.trustedDeviceId
    };
  }


  const trustedDeviceId =
    'TD-' +
    Utilities.getUuid();


  sheet.appendRow([
    trustedDeviceId,
    user.userId,
    user.email,
    hashATSDeviceToken_(
      deviceToken
    ),
    'Trusted browser',
    now,
    now,
    'Active',
    '',
    ''
  ]);


  logATSActivity_({
    email:
      user.email,

    fullName:
      user.fullName,

    role:
      user.role,

    action:
      'TRUSTED_DEVICE_ADDED',

    entityType:
      'Trusted Device',

    entityId:
      trustedDeviceId,

    details:
      'Browser added as a trusted ATS device'
  });


  return {
    trustedDeviceId:
      trustedDeviceId
  };
}


function updateTrustedATSDeviceLastUsed_(
  row
) {
  const ss =
    SpreadsheetApp.openById(
      CONFIG.SPREADSHEET_ID
    );

  const sheet =
    ss.getSheetByName(
      'Trusted Devices'
    );


  if (
    !sheet ||
    !row
  ) {
    return;
  }


  sheet
    .getRange(
      row,
      7
    )
    .setValue(
      new Date()
    );
}


function hashATSDeviceToken_(
  token
) {
  const bytes =
    Utilities.computeDigest(
      Utilities.DigestAlgorithm.SHA_256,
      String(token || ''),
      Utilities.Charset.UTF_8
    );


  return bytes
    .map(
      function(byte) {
        const value =
          byte < 0
            ? byte + 256
            : byte;

        return (
          '0' +
          value.toString(16)
        ).slice(-2);
      }
    )
    .join('');
}


function findATSUserByEmail_(
  email
) {
  const ss =
    getSpreadsheet_();

  const sheet =
    ss.getSheetByName(
      'Users & Access'
    );

  if (!sheet) {
    throw new Error(
      'Users & Access sheet was not found.'
    );
  }

  const values =
    sheet
      .getDataRange()
      .getValues();

  if (
    values.length < 2
  ) {
    return null;
  }

  const headers =
    values[0];

  const column =
    function(name) {
      return headers.indexOf(name);
    };


  for (
    let i = 1;
    i < values.length;
    i++
  ) {

    const rowEmail =
      String(
        values[i][
          column('Email')
        ] || ''
      )
        .trim()
        .toLowerCase();

    if (
      rowEmail !== email
    ) {
      continue;
    }

    return {
      rowNumber:
        i + 1,

      userId:
        values[i][
          column('User ID')
        ] || '',

      fullName:
        values[i][
          column('Full Name')
        ] || '',

      email:
        rowEmail,

      role:
        values[i][
          column('Role')
        ] || '',

      status:
        values[i][
          column('Status')
        ] || '',

      assignedClients:
        values[i][
          column(
            'Assigned Clients'
          )
        ] || '',

      assignedRefs:
        values[i][
          column(
            'Assigned Ref #s'
          )
        ] || '',

      permissions: {
        canCreatePositions:
          String(
            values[i][
              column(
                'Can Create Positions'
              )
            ] || ''
          )
            .trim()
            .toLowerCase() ===
          'yes',

        canViewTalentPool:
          String(
            values[i][
              column(
                'Can View Talent Pool'
              )
            ] || ''
          )
            .trim()
            .toLowerCase() ===
          'yes',

        canExportReports:
          String(
            values[i][
              column(
                'Can Export Reports'
              )
            ] || ''
          )
            .trim()
            .toLowerCase() ===
          'yes'
      }
    };
  }

  return null;
}


function updateLastLogin_(
  email
) {
  const ss =
    getSpreadsheet_();

  const sheet =
    ss.getSheetByName(
      'Users & Access'
    );

  const values =
    sheet
      .getDataRange()
      .getValues();

  const headers =
    values[0];

  const emailIndex =
    headers.indexOf(
      'Email'
    );

  const loginIndex =
    headers.indexOf(
      'Last Login'
    );


  for (
    let i = 1;
    i < values.length;
    i++
  ) {

    const rowEmail =
      String(
        values[i][emailIndex] || ''
      )
        .trim()
        .toLowerCase();

    if (
      rowEmail === email
    ) {
      sheet
        .getRange(
          i + 1,
          loginIndex + 1
        )
        .setValue(
          new Date()
        );

      return;
    }
  }
}


function logATSActivity_(data) {
  const ss =
    getSpreadsheet_();

  const sheet =
    ss.getSheetByName(
      'Activity Log'
    );

  if (!sheet) {
    return;
  }

  sheet.appendRow([
    new Date(),
    data.email || '',
    data.fullName || '',
    data.role || '',
    data.action || '',
    data.entityType || '',
    data.entityId || '',
    data.client || '',
    data.details || '',
    data.previousValue || '',
    data.newValue || ''
  ]);
}
