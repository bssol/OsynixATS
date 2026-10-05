function doGet(e) {

  const page =
    e &&
    e.parameter &&
    e.parameter.page
      ? String(
          e.parameter.page
        )
          .trim()
          .toLowerCase()
      : 'login';


  const sessionToken =
    e &&
    e.parameter &&
    e.parameter.session
      ? String(
          e.parameter.session
        ).trim()
      : '';


  if (
    page !== 'login'
  ) {

    const session =
      validateATSSession(
        sessionToken
      );


    if (
      !session ||
      !session.valid ||
      !session.user
    ) {

      return HtmlService
        .createTemplateFromFile(
          'Login'
        )
        .evaluate()
        .setTitle(
          'Osynix ATS - Login'
        )
        .setXFrameOptionsMode(
          HtmlService
            .XFrameOptionsMode
            .ALLOWALL
        );
    }


    const user =
      session.user;

    const role =
      String(
        user.role || ''
      )
        .trim()
        .toLowerCase();


    if (
      page === 'position' &&
      (
        !user.permissions ||
        user.permissions
          .canCreatePositions !== true
      )
    ) {

      return HtmlService
        .createTemplateFromFile(
          'Index'
        )
        .evaluate()
        .setTitle(
          'Osynix ATS - Candidate Assessment'
        )
        .setXFrameOptionsMode(
          HtmlService
            .XFrameOptionsMode
            .ALLOWALL
        );
    }


    if (
(
page === 'dashboard' ||
page === 'managepositions' ||
page === 'candidatedatabase' ||
page === 'talentpool'
) &&
role !== 'admin' &&
role !== 'recruiter'
) {


      return HtmlService
        .createTemplateFromFile(
          'Index'
        )
        .evaluate()
        .setTitle(
          'Osynix ATS - Candidate Assessment'
        )
        .setXFrameOptionsMode(
          HtmlService
            .XFrameOptionsMode
            .ALLOWALL
        );
    }

  }


  let fileName =
    'Login';

  let pageTitle =
    'Osynix ATS - Login';


  if (
    page === 'candidate'
  ) {
    fileName =
      'Index';

    pageTitle =
      'Osynix ATS - Candidate Assessment';
  }


  if (
    page === 'position'
  ) {
    fileName =
      'Position';

    pageTitle =
      'Osynix ATS - Create Position';
  }


 if (
  page === 'dashboard'
) {
  fileName =
    'Dashboard';

  pageTitle =
    'Osynix ATS - Dashboard';
}

if (
page === 'managepositions'
) {
fileName =
'ManagePositions';

pageTitle =
'Osynix ATS - Manage Positions';
}
if (
  page === 'candidatedatabase'
) {

  fileName =
    'CandidateDatabase';

  pageTitle =
    'Osynix ATS - Candidate Database';

}
if (
  page === 'talentpool'
) {

  fileName =
    'TalentPool';

  pageTitle =
    'Osynix ATS - Talent Pool';

}

  return HtmlService
    .createTemplateFromFile(
      fileName
    )
    .evaluate()
    .setTitle(
      pageTitle
    )
    .setXFrameOptionsMode(
      HtmlService
        .XFrameOptionsMode
        .ALLOWALL
    );
}


function include(filename) {
  return HtmlService
    .createHtmlOutputFromFile(filename)
    .getContent();
}


function getBootstrapData(sessionToken) {
  requireATSOperationalUser_(sessionToken);

  return {
    positions: getActivePositions_(),
    stages: getSettingsColumn_('Recruitment Stage'),
    interviewStatuses: getSettingsColumn_('Interview Status'),
    clientStatuses: getSettingsColumn_('Client Status')
  };
}


function getPositionDetails(sessionToken, refNo) {
  requireATSOperationalUser_(sessionToken);

  const position =
    getPositionByRef_(
      refNo
    );

  const criteria =
    getCriteriaByRef_(
      refNo
    );

  if (!position) {
    throw new Error(
      'Position not found for Ref #' +
      refNo
    );
  }


  const ss =
    getSpreadsheet_();

  const sheet =
    ss.getSheetByName(
      CONFIG.SHEETS.POSITIONS
    );

  if (!sheet) {
    throw new Error(
      'Positions Master sheet was not found.'
    );
  }


  const lastRow =
    sheet.getLastRow();

  const lastColumn =
    sheet.getLastColumn();

  const values =
    sheet.getRange(
      1,
      1,
      lastRow,
      lastColumn
    ).getDisplayValues();

  const headers =
    values[0];

  const refIndex =
    headers.indexOf(
      'Ref #'
    );

  const jdIndex =
    headers.indexOf(
      'JD / File Link'
    );


  if (
    refIndex !== -1 &&
    jdIndex !== -1
  ) {

    const targetRef =
      String(refNo || '')
        .trim()
        .padStart(3, '0');


    for (
      let i = 1;
      i < values.length;
      i++
    ) {

      const rowRef =
        String(
          values[i][refIndex] || ''
        )
          .trim()
          .padStart(3, '0');


      if (
        rowRef !== targetRef
      ) {
        continue;
      }


      const jdCell =
        sheet.getRange(
          i + 1,
          jdIndex + 1
        );

      let jdUrl = '';

      const richText =
        jdCell.getRichTextValue();


      if (
        richText &&
        richText.getLinkUrl()
      ) {
        jdUrl =
          richText.getLinkUrl();
      }


      if (!jdUrl) {
        const rawValue =
          String(
            jdCell.getValue() || ''
          ).trim();

        if (
          rawValue.startsWith(
            'http://'
          ) ||
          rawValue.startsWith(
            'https://'
          )
        ) {
          jdUrl =
            rawValue;
        }
      }


      position[
        'JD / File Link'
      ] = jdUrl;

      break;
    }
  }


  return {
    position:
      position,

    criteria:
      criteria
  };
}

function getWebAppUrl() {
  return 'https://script.google.com/macros/s/AKfycbxLxFOev8XGW622Z9Xj7Vgr8PhrHvsChDedYs9vfC7uflI__xuNbUQJTfCFy7vYRV9VOg/exec';
}

function diagnoseATSWriteTarget() {
  const ss = getSpreadsheet_();

  const candidatesSheet =
    ss.getSheetByName(CONFIG.SHEETS.CANDIDATES);

  const talentSheet =
    ss.getSheetByName(CONFIG.SHEETS.TALENT_POOL);

  Logger.log(
    'Spreadsheet Name: ' + ss.getName()
  );

  Logger.log(
    'Spreadsheet ID: ' + ss.getId()
  );

  Logger.log(
    'Candidates Sheet: ' + CONFIG.SHEETS.CANDIDATES
  );

  Logger.log(
    'Candidates Last Row: ' +
    (candidatesSheet ? candidatesSheet.getLastRow() : 'NOT FOUND')
  );

  Logger.log(
    'Talent Pool Sheet: ' + CONFIG.SHEETS.TALENT_POOL
  );

  Logger.log(
    'Talent Pool Last Row: ' +
    (talentSheet ? talentSheet.getLastRow() : 'NOT FOUND')
  );

  return {
    spreadsheetName: ss.getName(),
    spreadsheetId: ss.getId(),
    candidatesLastRow:
      candidatesSheet ? candidatesSheet.getLastRow() : null,
    talentLastRow:
      talentSheet ? talentSheet.getLastRow() : null
  };
}