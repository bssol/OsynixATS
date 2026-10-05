function sheetToObjects_(sheetName) {
  const sh = getSpreadsheet_().getSheetByName(sheetName);

  if (!sh) {
    throw new Error('Missing sheet: ' + sheetName);
  }

  const values = sh.getDataRange().getDisplayValues();

  if (!values.length) {
    return [];
  }

  const headers = values[0];

  return values
    .slice(1)
    .filter(row => row.some(value => value !== ''))
    .map(row => {
      const obj = {};

      headers.forEach((header, index) => {
        obj[header] = row[index];
      });

      return obj;
    });
}


function getActivePositions_() {
  return sheetToObjects_(CONFIG.SHEETS.POSITIONS)
    .filter(row =>
      row['Ref #'] &&
      row['Status'] === 'Active'
    )
    .map(row => ({
      refNo: row['Ref #'],
      client: row['Client'],
      position: row['Position'],
      location: row['Location'],
      priority: row['Priority']
    }));
}


function getPositionByRef_(refNo) {
  const row = sheetToObjects_(CONFIG.SHEETS.POSITIONS)
    .find(row =>
      String(row['Ref #']) === String(refNo)
    );

  if (!row) {
    throw new Error(
      'Position not found for Ref #' + refNo
    );
  }

  return row;
}


function getCriteriaByRef_(refNo) {
  return sheetToObjects_(CONFIG.SHEETS.CRITERIA)
    .filter(row =>
      String(row['Ref #']) === String(refNo)
    )
    .map(row => ({
      criterion: row['Criterion'],
      weight: Number(row['Weight %'] || 0),
      knockout: row['Knockout?'],
      evidenceExpected: row['Evidence Expected'],
      scoringGuidance: row['Scoring Guidance'],
      notes: row['Notes']
    }));
}


function getSettingsColumn_(header) {
  const sh = getSpreadsheet_()
    .getSheetByName(CONFIG.SHEETS.SETTINGS);

  const data = sh
    .getDataRange()
    .getDisplayValues();

  const columnIndex =
    data[0].indexOf(header);

  if (columnIndex < 0) {
    return [];
  }

  return data
    .slice(1)
    .map(row => row[columnIndex])
    .filter(Boolean);
}


function findTalentCandidate_(email, phone, name) {
  const rows =
    sheetToObjects_(
      CONFIG.SHEETS.TALENT_POOL
    );

  const normalize =
    value =>
      String(value || '')
        .trim()
        .toLowerCase();

  if (email) {
    const normalizedEmail =
      normalize(email);

    const match =
      rows.find(row =>
        normalize(row['Email ID']) === normalizedEmail
      );

    if (match) {
      return match;
    }
  }

  if (phone) {
    const normalizedPhone =
      String(phone).replace(/\D/g, '');

    const match =
      rows.find(row =>
        String(
          row['Mobile / Phone Number'] || ''
        ).replace(/\D/g, '') === normalizedPhone
      );

    if (match) {
      return match;
    }
  }

  if (name) {
    const normalizedName =
      normalize(name);

    const matches =
      rows.filter(row =>
        normalize(row['Candidate Name']) === normalizedName
      );

    if (matches.length === 1) {
      return matches[0];
    }
  }

  return null;
}


function findExistingAssessment_(candidateId, refNo) {
  return (
    sheetToObjects_(CONFIG.SHEETS.CANDIDATES)
      .find(row =>
        String(row['Candidate ID']) === String(candidateId) &&
        String(row['Ref #']) === String(refNo)
      ) || null
  );
}


function nextCandidateId_() {
  const sh =
    getSpreadsheet_()
      .getSheetByName(
        CONFIG.SHEETS.TALENT_POOL
      );

  const lastRow =
    sh.getLastRow();

  if (lastRow < 2) {
    return 'CAND-001';
  }

  const ids =
    sh
      .getRange(
        2,
        1,
        lastRow - 1,
        1
      )
      .getDisplayValues()
      .flat();

  const maxNumber =
    ids.reduce(
      (highest, id) => {

        const match =
          /^CAND-(\d+)$/.exec(id);

        if (!match) {
          return highest;
        }

        return Math.max(
          highest,
          Number(match[1])
        );
      },
      0
    );

  return (
    'CAND-' +
    String(maxNumber + 1)
      .padStart(3, '0')
  );
}


function appendObjectRow_(sheetName, obj) {
  const sh =
    getSpreadsheet_()
      .getSheetByName(sheetName);

  const headers =
    sh
      .getRange(
        1,
        1,
        1,
        sh.getLastColumn()
      )
      .getDisplayValues()[0];

  const row =
    headers.map(header =>
      Object.prototype.hasOwnProperty.call(
        obj,
        header
      )
        ? obj[header]
        : ''
    );

  sh.appendRow(row);
}


function updateObjectRowByKeys_(
  sheetName,
  keyFields,
  obj
) {
  const sh =
    getSpreadsheet_()
      .getSheetByName(sheetName);

  const values =
    sh
      .getDataRange()
      .getDisplayValues();

  const headers =
    values[0];

  const keyIndexes =
    keyFields.map(
      key =>
        headers.indexOf(key)
    );

  const targetIndex =
    values
      .slice(1)
      .findIndex(row =>
        keyFields.every(
          (key, index) =>
            String(
              row[keyIndexes[index]]
            ) ===
            String(obj[key])
        )
      );

  if (targetIndex < 0) {
    return false;
  }

  const rowNumber =
    targetIndex + 2;

  headers.forEach(
    (header, columnIndex) => {

      if (
        Object.prototype.hasOwnProperty.call(
          obj,
          header
        ) &&
        header !== 'Decision'
      ) {
        sh
          .getRange(
            rowNumber,
            columnIndex + 1
          )
          .setValue(
            obj[header]
          );
      }
    }
  );

  return true;
}