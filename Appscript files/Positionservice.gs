/**
 * Osynix ATS - Position / JD Module
 *
 * Purpose:
 * 1) Analyze an uploaded JD and return a DRAFT position + criteria.
 * 2) Write the approved position and criteria to the live ATS only when
 *    createPosition() is explicitly called by the web UI.
 *
 * IMPORTANT:
 * analyzeJobDescription() does NOT write anything to Google Sheets.
 */


function analyzeJobDescription(sessionToken, payload) {
  requireATSOperationalUser_(sessionToken);

  if (!payload) {
    throw new Error('Missing JD analysis payload.');
  }

  if (!payload.fileBase64 || !payload.fileName || !payload.mimeType) {
    throw new Error('Please upload a JD file.');
  }

  const client = String(payload.client || '').trim();
  const position = String(payload.position || '').trim();
  const location = String(payload.location || '').trim();

  const prompt = buildJDAnalysisPrompt_(
    client,
    position,
    location
  );

  const body = {
    model: CONFIG.OPENAI_MODEL,

    input: [{
      role: 'user',
      content: [
        {
          type: 'input_file',
          filename: payload.fileName,
          file_data:
            `data:${payload.mimeType};base64,${payload.fileBase64}`
        },
        {
          type: 'input_text',
          text: prompt
        }
      ]
    }],

    text: {
      format: {
        type: 'json_schema',
        name: 'osynix_jd_analysis',
        strict: true,

        schema: {
          type: 'object',
          additionalProperties: false,

          properties: {
            client: {
              type: ['string', 'null']
            },

            position: {
              type: 'string'
            },

            location: {
              type: ['string', 'null']
            },

            position_summary: {
              type: 'string'
            },

            criteria: {
              type: 'array',
              minItems: 4,
              maxItems: 10,

              items: {
                type: 'object',
                additionalProperties: false,

                properties: {
                  criterion: {
                    type: 'string'
                  },

                  weight_percent: {
                    type: 'number',
                    minimum: 1,
                    maximum: 60
                  },

                  knockout: {
                    type: 'string',
                    enum: ['Yes', 'No']
                  },

                  evidence_expected: {
                    type: 'string'
                  },

                  scoring_guidance: {
                    type: 'string'
                  },

                  notes: {
                    type: 'string'
                  },

                  rationale: {
                    type: 'string'
                  }
                },

                required: [
                  'criterion',
                  'weight_percent',
                  'knockout',
                  'evidence_expected',
                  'scoring_guidance',
                  'notes',
                  'rationale'
                ]
              }
            }
          },

          required: [
            'client',
            'position',
            'location',
            'position_summary',
            'criteria'
          ]
        }
      }
    }
  };

  const response = UrlFetchApp.fetch(
    'https://api.openai.com/v1/responses',
    {
      method: 'post',
      contentType: 'application/json',

      headers: {
        Authorization:
          'Bearer ' + getOpenAIKey_()
      },

      payload:
        JSON.stringify(body),

      muteHttpExceptions: true
    }
  );

  const status =
    response.getResponseCode();

  const responseText =
    response.getContentText();

  if (status < 200 || status >= 300) {
    throw new Error(
      'OpenAI API error ' +
      status +
      ': ' +
      responseText
    );
  }

  const json =
    JSON.parse(responseText);

  let outputText =
    json.output_text || '';

  if (
    !outputText &&
    Array.isArray(json.output)
  ) {
    for (const item of json.output) {
      if (!Array.isArray(item.content)) {
        continue;
      }

      for (const content of item.content) {
        if (
          content.type === 'output_text' &&
          content.text
        ) {
          outputText += content.text;
        }
      }
    }
  }

  if (!outputText) {
    throw new Error(
      'No structured JD analysis returned.'
    );
  }

  const result =
    JSON.parse(outputText);

  result.criteria =
    normalizeJDWeights_(
      result.criteria || []
    );

  validateJDAnalysis_(
    result
  );

  return {
    client:
      client ||
      result.client ||
      '',

    position:
      position ||
      result.position ||
      '',

    location:
      location ||
      result.location ||
      '',

    position_summary:
      result.position_summary || '',

    criteria:
      result.criteria
  };
}


function buildJDAnalysisPrompt_(
  client,
  position,
  location
) {
  return `
You are the JD-analysis component of the Osynix ATS.

Your task is to convert the uploaded Job Description into a structured,
auditable candidate-assessment specification.

This output is only a DRAFT.
A recruiter/admin will review and edit it before anything is saved.

==================================================
OPTIONAL USER-SUPPLIED POSITION DATA
==================================================

Client:
${client || 'Not supplied'}

Position:
${position || 'Use the JD to determine the correct position title'}

Location:
${location || 'Not supplied'}

==================================================
OBJECTIVE
==================================================

Create role-specific assessment criteria that another AI can later use to
evaluate candidate CVs consistently.

The criteria must reflect what the JD actually requires.

Do NOT invent client requirements that are not supported by the JD.

Do NOT copy the JD into generic headings.

Convert the JD into a concise screening model.

==================================================
CRITERION DESIGN RULES
==================================================

Create between 4 and 10 criteria.

Each criterion must represent a materially distinct hiring dimension.

Good examples include:
- exact specialist/domain experience;
- market/geography exposure;
- operational ownership;
- leadership;
- client/customer management;
- technical capability;
- systems/tools;
- compliance;
- commercial responsibility.

Avoid duplicate or overlapping criteria.

Avoid vague criteria such as:
- "Good Candidate"
- "Communication Skills"
unless the JD makes them materially important and assessable.

==================================================
WEIGHTS
==================================================

Every criterion must have a Weight %.

All weights MUST total exactly 100%.

Higher weights should be assigned to requirements that materially determine
success in the role.

Do not overweight generic supporting skills above the role's core capability.

==================================================
KNOCKOUT CRITERIA
==================================================

Use Knockout = Yes only when the requirement is genuinely mandatory.

Examples:
- a legally required license;
- a clearly mandatory market exposure;
- a clearly mandatory specialist domain;
- a mandatory certification;
- an explicit non-negotiable client requirement.

Do NOT mark something as knockout merely because it is desirable.

Keep the number of knockout criteria limited.

==================================================
EVIDENCE EXPECTED
==================================================

For each criterion, explain what CV evidence would actually demonstrate the
requirement.

Evidence Expected should be concrete.

Example:

Bad:
"Good operations experience."

Better:
"End-to-end ownership of daily operations, SOPs, KPIs, service delivery,
escalation handling, staffing and operational performance."

==================================================
SCORING GUIDANCE
==================================================

Explain how strong vs weak evidence should be interpreted.

Focus on:
- direct vs transferable experience;
- ownership level;
- scale;
- seniority;
- duration;
- recency;
- complexity;
- measurable results;
- domain similarity.

The guidance should help another evaluator distinguish a 4/10 candidate from
an 8/10 candidate.

==================================================
NOTES
==================================================

Use Notes for specific interpretation rules.

Examples:
- "Generic logistics is transferable but not equivalent to direct chauffeur
operations."
- "International exposure does not automatically mean US-market exposure."
- "Tool names should score only when operational use is evidenced."

Notes should prevent predictable false positives.

==================================================
IMPORTANT
==================================================

The future candidate-assessment engine will use these fields as the role's
source of truth:

Criterion
Weight %
Knockout?
Evidence Expected
Scoring Guidance
Notes

Therefore, be precise and conservative.

Return only the structured response requested by the schema.
`.trim();
}


/**
 * Writes an APPROVED position and its APPROVED criteria to the live ATS.
 *
 * This function should only be called after the recruiter/admin explicitly
 * clicks Create Position in the web UI.
 */
function createPosition(payload) {
  if (!payload) {
    throw new Error(
      'Missing position creation payload.'
    );
  }

  const sessionToken =
    String(
      payload.sessionToken || ''
    ).trim();

  requireCanCreatePositions_(
    sessionToken
  );

const client =
  canonicalizeClientName_(
    payload.client
  );

  const position =
    String(payload.position || '').trim();

  const location =
    String(payload.location || '').trim();

  const priority =
    String(payload.priority || 'Medium').trim();

  const hiringTargetRaw =
    Number(payload.hiringTarget || 1);

  const hiringTarget =
    isNaN(hiringTargetRaw) ||
    hiringTargetRaw < 1
      ? 1
      : Math.round(hiringTargetRaw);

  const notes =
    String(payload.notes || '').trim();

  const criteria =
    Array.isArray(payload.criteria)
      ? payload.criteria
      : [];

  if (!client) {
    throw new Error(
      'Client is required.'
    );
  }

  if (!position) {
    throw new Error(
      'Position title is required.'
    );
  }

  if (!criteria.length) {
    throw new Error(
      'At least one approved JD criterion is required.'
    );
  }

  const normalizedCriteria =
    normalizeJDWeights_(
      criteria.map(item => ({
        criterion:
          String(item.criterion || '').trim(),

        weight_percent:
          Number(
            item.weight_percent ||
            item.weight ||
            0
          ),

        knockout:
          String(item.knockout || 'No').trim(),

        evidence_expected:
          String(
            item.evidence_expected ||
            item.evidenceExpected ||
            ''
          ).trim(),

        scoring_guidance:
          String(
            item.scoring_guidance ||
            item.scoringGuidance ||
            ''
          ).trim(),

        notes:
          String(item.notes || '').trim(),

        rationale:
          String(item.rationale || '').trim()
      }))
    );

  validateJDAnalysis_({
    position:
      position,

    criteria:
      normalizedCriteria
  });

  const ss =
    getSpreadsheet_();

  const positionsSheet =
    ss.getSheetByName(
      CONFIG.SHEETS.POSITIONS
    );

  const criteriaSheet =
    ss.getSheetByName(
      CONFIG.SHEETS.CRITERIA
    );

  if (!positionsSheet) {
    throw new Error(
      'Positions Master sheet was not found.'
    );
  }

  if (!criteriaSheet) {
    throw new Error(
      'JD Criteria sheet was not found.'
    );
  }
const jdHash =
  calculateJDHash_(
    payload.jdFile &&
    payload.jdFile.fileBase64
  );

const duplicate =
  findPositionDuplicate_(
    client,
    position,
    jdHash
  );

if (duplicate) {

  if (
    duplicate.duplicateType ===
    'JD'
  ) {
    throw new Error(
      'Duplicate JD detected. This exact JD already exists under Ref #' +
      duplicate.refNo +
      ' - ' +
      duplicate.position +
      '. Please use the existing position instead of creating a duplicate.'
    );
  }

  if (
    duplicate.duplicateType ===
    'POSITION'
  ) {
    throw new Error(
      'Duplicate active position detected. Ref #' +
      duplicate.refNo +
      ' - ' +
      duplicate.position +
      ' is already active for this client.'
    );
  }
}
  const refNo =
    nextRefNo_();

  const jdDriveResult =
  savePositionJDToDrive_(
    client,
    refNo,
    position,
    payload.jdFile
  );

const jdFileUrl =
  jdDriveResult.fileUrl;  

  const now =
    new Date();

  const today =
    Utilities.formatDate(
      now,
      CONFIG.TIMEZONE,
      'yyyy-MM-dd'
    );

  const positionRow = {
    'Ref #':
      "'" + refNo,

    'Client':
      client,

    'Position':
      position,

    'Location':
      location,

    'Open Date':
      today,

    'Status':
      'Active',

    'Priority':
      priority,

    'Hiring Target':
      hiringTarget,

    'Profiles Screened':
      0,

    'Shortlisted':
      0,

    'Client Interviews':
      0,

    'Offers':
      0,

    'Hired':
      0,

    'Average ATS Match':
      '',

    'Hiring Manager / Contact':
      '',

    'JD / File Link':
      jdFileUrl,
    'JD File Hash':
      jdHash,

    'Notes':
      notes,

    'Last Updated':
      now
  };

  // IMPORTANT:
  // appendObjectRow_ expects the SHEET NAME, not a Sheet object.
  appendObjectRow_(
    CONFIG.SHEETS.POSITIONS,
    positionRow
  );

  normalizedCriteria.forEach(item => {
    const row = {
      'Ref #':
       "'" + refNo,

      'Criterion':
        item.criterion,

      'Weight %':
        item.weight_percent,

      'Knockout?':
        item.knockout,

      'Evidence Expected':
        item.evidence_expected,

      'Scoring Guidance':
        item.scoring_guidance,

      'Notes':
        item.notes
    };

    // IMPORTANT:
    // appendObjectRow_ expects the SHEET NAME, not a Sheet object.
    appendObjectRow_(
      CONFIG.SHEETS.CRITERIA,
      row
    );
  });

  SpreadsheetApp.flush();

  return {
    success:
      true,

    refNo:
      refNo,

    client:
      client,

    position:
      position,

    criteriaCount:
      normalizedCriteria.length
  };
}
function getExistingClients(sessionToken) {
  requireATSOperationalUser_(sessionToken);

  return getExistingClientsInternal_();
}


function getExistingClientsInternal_() {
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

  const values =
    sheet.getDataRange()
      .getDisplayValues();

  if (values.length < 2) {
    return [];
  }

  const headers =
    values[0].map(
      function(value) {
        return String(value || '')
          .trim();
      }
    );

  const clientIndex =
    headers.indexOf(
      'Client'
    );

  if (clientIndex < 0) {
    throw new Error(
      'Client column was not found in Positions Master.'
    );
  }

  const clientMap = {};

  values
    .slice(1)
    .forEach(
      function(row) {
        const client =
          String(
            row[clientIndex] || ''
          ).trim();

        if (!client) {
          return;
        }

        const key =
          normalizeClientNameServer_(
            client
          );

        if (
          !Object.prototype
            .hasOwnProperty.call(
              clientMap,
              key
            )
        ) {
          clientMap[key] =
            client;
        }
      }
    );

  return Object.keys(
    clientMap
  )
    .map(
      function(key) {
        return clientMap[key];
      }
    )
    .sort(
      function(a, b) {
        return a.localeCompare(b);
      }
    );
}


function canonicalizeClientName_(
  proposedClient
) {
  const clean =
    String(
      proposedClient || ''
    ).trim();

  if (!clean) {
    return '';
  }

  const key =
    normalizeClientNameServer_(
      clean
    );

  const existing =
    getExistingClientsInternal_();

  for (
    let i = 0;
    i < existing.length;
    i++
  ) {
    if (
      normalizeClientNameServer_(
        existing[i]
      ) ===
      key
    ) {
      return existing[i];
    }
  }

  return clean;
}


function normalizeClientNameServer_(
  value
) {
  return String(value || '')
    .toLowerCase()
    .replace(
      /[^a-z0-9]+/g,
      ' '
    )
    .replace(
      /\s+/g,
      ' '
    )
    .trim();
}
function savePositionJDToDrive_(
  client,
  refNo,
  position,
  jdFile
) {
  if (
    !jdFile ||
    !jdFile.fileBase64 ||
    !jdFile.fileName
  ) {
    throw new Error(
      'Original JD file is missing. The position was not created.'
    );
  }

  const rootFolder =
    getOrCreateRootFolder_(
      'Osynix ATS Recruitment'
    );

  const clientFolder =
    getOrCreateChildFolder_(
      rootFolder,
      sanitizeDriveFolderName_(
        client
      )
    );

  const positionFolderName =
    refNo +
    ' - ' +
    sanitizeDriveFolderName_(
      position
    );

  const positionFolder =
    getOrCreateChildFolder_(
      clientFolder,
      positionFolderName
    );

  const jdFolder =
    getOrCreateChildFolder_(
      positionFolder,
      'Job Description'
    );

  const bytes =
    Utilities.base64Decode(
      jdFile.fileBase64
    );

  const blob =
    Utilities.newBlob(
      bytes,
      jdFile.mimeType ||
        'application/octet-stream',
      jdFile.fileName
    );

  const file =
    jdFolder.createFile(
      blob
    );

  return {
    fileId:
      file.getId(),

    fileName:
      file.getName(),

    fileUrl:
      file.getUrl(),

    folderUrl:
      jdFolder.getUrl()
  };
}


function getOrCreateRootFolder_(
  folderName
) {
  const folders =
    DriveApp.getFoldersByName(
      folderName
    );

  if (folders.hasNext()) {
    return folders.next();
  }

  return DriveApp.createFolder(
    folderName
  );
}


function getOrCreateChildFolder_(
  parentFolder,
  folderName
) {
  const folders =
    parentFolder.getFoldersByName(
      folderName
    );

  if (folders.hasNext()) {
    return folders.next();
  }

  return parentFolder.createFolder(
    folderName
  );
}


function sanitizeDriveFolderName_(
  value
) {
  return String(value || '')
    .trim()
    .replace(
      /[\\\/]+/g,
      '-'
    )
    .replace(
      /\s+/g,
      ' '
    );
}

function nextRefNo_() {
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

  const values =
    sheet.getDataRange()
      .getDisplayValues();

  if (values.length < 2) {
    return '001';
  }

  const headers =
    values[0];

  const refIndex =
    headers.indexOf('Ref #');

  if (refIndex < 0) {
    throw new Error(
      'Ref # column was not found in Positions Master.'
    );
  }

  let maxRef = 0;

  values
    .slice(1)
    .forEach(row => {
      const raw =
        String(row[refIndex] || '')
          .trim();

      const numeric =
        parseInt(
          raw.replace(/[^\d]/g, ''),
          10
        );

      if (
        !isNaN(numeric) &&
        numeric > maxRef
      ) {
        maxRef =
          numeric;
      }
    });

  return String(maxRef + 1)
    .padStart(3, '0');
}


function normalizeJDWeights_(criteria) {
  if (
    !Array.isArray(criteria) ||
    !criteria.length
  ) {
    return [];
  }

  const cleaned =
    criteria.map(item => ({
      criterion:
        String(item.criterion || '').trim(),

      weight_percent:
        Number(
          item.weight_percent ||
          item.weight ||
          0
        ),

      knockout:
        String(item.knockout || 'No')
          .trim() === 'Yes'
          ? 'Yes'
          : 'No',

      evidence_expected:
        String(
          item.evidence_expected ||
          item.evidenceExpected ||
          ''
        ).trim(),

      scoring_guidance:
        String(
          item.scoring_guidance ||
          item.scoringGuidance ||
          ''
        ).trim(),

      notes:
        String(item.notes || '').trim(),

      rationale:
        String(item.rationale || '').trim()
    }));

  let total =
    cleaned.reduce(
      (sum, item) =>
        sum +
        (
          isNaN(item.weight_percent)
            ? 0
            : item.weight_percent
        ),
      0
    );

  if (total <= 0) {
    const equalWeight =
      100 / cleaned.length;

    cleaned.forEach(item => {
      item.weight_percent =
        equalWeight;
    });

    total = 100;
  }

  cleaned.forEach(item => {
    item.weight_percent =
      (
        item.weight_percent /
        total
      ) * 100;
  });

  cleaned.forEach(item => {
    item.weight_percent =
      Math.round(
        item.weight_percent
      );
  });

  const roundedTotal =
    cleaned.reduce(
      (sum, item) =>
        sum +
        item.weight_percent,
      0
    );

  const difference =
    100 -
    roundedTotal;

  if (difference !== 0) {
    let largestIndex = 0;

    for (
      let i = 1;
      i < cleaned.length;
      i++
    ) {
      if (
        cleaned[i].weight_percent >
        cleaned[largestIndex].weight_percent
      ) {
        largestIndex = i;
      }
    }

    cleaned[largestIndex].weight_percent +=
      difference;
  }

  return cleaned;
}


function validateJDAnalysis_(result) {
  if (!result) {
    throw new Error(
      'JD analysis result is empty.'
    );
  }

  if (
    !String(result.position || '').trim()
  ) {
    throw new Error(
      'Position title is missing from JD analysis.'
    );
  }

  if (
    !Array.isArray(result.criteria) ||
    !result.criteria.length
  ) {
    throw new Error(
      'No JD criteria were generated.'
    );
  }

  if (
    result.criteria.length < 4 ||
    result.criteria.length > 10
  ) {
    throw new Error(
      'JD Criteria must contain between 4 and 10 criteria.'
    );
  }

  const names = {};
  let totalWeight = 0;

  result.criteria.forEach(
    (item, index) => {
      const criterion =
        String(
          item.criterion || ''
        ).trim();

      if (!criterion) {
        throw new Error(
          'Criterion #' +
          (index + 1) +
          ' has no name.'
        );
      }

      const key =
        criterion.toLowerCase();

      if (names[key]) {
        throw new Error(
          'Duplicate criterion found: ' +
          criterion
        );
      }

      names[key] =
        true;

      const weight =
        Number(
          item.weight_percent ||
          item.weight ||
          0
        );

      if (
        isNaN(weight) ||
        weight <= 0
      ) {
        throw new Error(
          'Invalid weight for criterion: ' +
          criterion
        );
      }

      totalWeight +=
        weight;

      const knockout =
        String(
          item.knockout || 'No'
        ).trim();

      if (
        knockout !== 'Yes' &&
        knockout !== 'No'
      ) {
        throw new Error(
          'Knockout must be Yes or No for criterion: ' +
          criterion
        );
      }

      if (
        !String(
          item.evidence_expected ||
          item.evidenceExpected ||
          ''
        ).trim()
      ) {
        throw new Error(
          'Evidence Expected is missing for criterion: ' +
          criterion
        );
      }

      if (
        !String(
          item.scoring_guidance ||
          item.scoringGuidance ||
          ''
        ).trim()
      ) {
        throw new Error(
          'Scoring Guidance is missing for criterion: ' +
          criterion
        );
      }
    }
  );

  if (
    Math.round(totalWeight) !== 100
  ) {
    throw new Error(
      'JD Criteria weights must total 100%. Current total: ' +
      totalWeight
    );
  }

  return true;
}

function authorizeDriveAccess() {
  const folders =
    DriveApp.getFoldersByName(
      'Osynix ATS Recruitment'
    );

  return folders.hasNext();
}

function calculateJDHash_(
  fileBase64
) {
  if (!fileBase64) {
    throw new Error(
      'JD file data is missing.'
    );
  }

  const bytes =
    Utilities.base64Decode(
      fileBase64
    );

  const digest =
    Utilities.computeDigest(
      Utilities.DigestAlgorithm.SHA_256,
      bytes
    );

  return digest
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


function normalizePositionForDuplicate_(
  value
) {
  return String(value || '')
    .trim()
    .toLowerCase()
    .replace(
      /[^a-z0-9]+/g,
      ' '
    )
    .replace(
      /\s+/g,
      ' '
    )
    .trim();
}


function findPositionDuplicate_(
  client,
  position,
  jdHash
) {
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

  if (lastRow < 2) {
    return null;
  }

  const values =
    sheet.getRange(
      1,
      1,
      lastRow,
      19
    ).getValues();

  const headers =
    values[0];

  const refIndex =
    headers.indexOf('Ref #');

  const clientIndex =
    headers.indexOf('Client');

  const positionIndex =
    headers.indexOf('Position');

  const statusIndex =
    headers.indexOf('Status');

  const hashIndex =
    headers.indexOf(
      'JD File Hash'
    );

  if (
    refIndex === -1 ||
    clientIndex === -1 ||
    positionIndex === -1 ||
    statusIndex === -1 ||
    hashIndex === -1
  ) {
    throw new Error(
      'Duplicate-check columns are missing from Positions Master.'
    );
  }

  const normalizedClient =
    normalizeClientNameServer_(
      client
    );

  const normalizedPosition =
    normalizePositionForDuplicate_(
      position
    );

  for (
    let i = 1;
    i < values.length;
    i++
  ) {
    const row =
      values[i];

    const existingClient =
      normalizeClientNameServer_(
        row[clientIndex]
      );

    if (
      existingClient !==
      normalizedClient
    ) {
      continue;
    }

    const existingRef =
      String(
        row[refIndex] || ''
      ).trim();

    const existingPosition =
      String(
        row[positionIndex] || ''
      ).trim();

    const existingStatus =
      String(
        row[statusIndex] || ''
      )
        .trim()
        .toLowerCase();

    const existingHash =
      String(
        row[hashIndex] || ''
      ).trim();

    const samePosition =
      normalizePositionForDuplicate_(
        existingPosition
      ) ===
      normalizedPosition;

    const sameHash =
      existingHash &&
      jdHash &&
      existingHash === jdHash;

    if (
      sameHash
    ) {
      return {
        duplicateType:
          'JD',

        refNo:
          existingRef,

        position:
          existingPosition,

        status:
          row[statusIndex]
      };
    }

    if (
      samePosition &&
      existingStatus === 'active'
    ) {
      return {
        duplicateType:
          'POSITION',

        refNo:
          existingRef,

        position:
          existingPosition,

        status:
          row[statusIndex]
      };
    }
  }

  return null;
}
function backfillJDFileHashes() {
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

  if (lastRow < 2) {
    return;
  }

  const headers =
    sheet
      .getRange(
        1,
        1,
        1,
        sheet.getLastColumn()
      )
      .getValues()[0];

  const refIndex =
    headers.indexOf('Ref #');

  const linkIndex =
    headers.indexOf(
      'JD / File Link'
    );

  const hashIndex =
    headers.indexOf(
      'JD File Hash'
    );

  if (
    linkIndex === -1 ||
    hashIndex === -1
  ) {
    throw new Error(
      'JD / File Link or JD File Hash column is missing.'
    );
  }

  for (
    let row = 2;
    row <= lastRow;
    row++
  ) {

    const hashCell =
      sheet.getRange(
        row,
        hashIndex + 1
      );

    if (
      String(
        hashCell.getValue() || ''
      ).trim()
    ) {
      continue;
    }

    const linkCell =
      sheet.getRange(
        row,
        linkIndex + 1
      );

    let fileUrl =
      String(
        linkCell.getValue() || ''
      ).trim();

    const richText =
      linkCell.getRichTextValue();

    if (
      richText &&
      richText.getLinkUrl()
    ) {
      fileUrl =
        richText.getLinkUrl();
    }

    const match =
      fileUrl.match(
        /[-\w]{25,}/
      );

    if (!match) {
      continue;
    }

    const fileId =
      match[0];

    const file =
      DriveApp.getFileById(
        fileId
      );

    const bytes =
      file
        .getBlob()
        .getBytes();

    const digest =
      Utilities.computeDigest(
        Utilities.DigestAlgorithm.SHA_256,
        bytes
      );

    const hash =
      digest
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

    hashCell.setValue(
      hash
    );
  }
}