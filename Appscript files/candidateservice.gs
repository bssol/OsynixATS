/**
 
 * Osynix ATS - CandidateService.gs
 
 *
 
 * Saves vacancy assessment + permanent Talent Pool profile.
 
 *
 
 * Same person + same Ref # => update
 
 * Same person + different Ref # => reuse Candidate ID + new vacancy assessment
 
 * Duplicate order => Email, Phone, unique normalized Name
 
 * New records => first genuinely empty Candidate ID row
 
 *
 
 * FIX IN THIS VERSION:
 
 * - Seniority Level is normalized to the exact controlled values
 
 *   allowed by Lists & Settings so Talent Pool saves do not fail
 
 *   data validation in column M.
 
 */
 
 
 
 
 
function saveAssessment(
 
  sessionToken,
 
  assessment,
 
  cvUpload
 
) {
 
  requireATSOperationalUser_(sessionToken);
 
 
 
  if (!assessment) {
 
    throw new Error(
 
      'Missing candidate assessment payload.'
 
    );
 
  }
 
 
 
  const now =
 
    new Date();
 
 
 
  const today =
 
    Utilities.formatDate(
 
      now,
 
      CONFIG.TIMEZONE,
 
      'yyyy-MM-dd'
 
    );
 
 
 
  const refNo =
 
    candNormalizeRefNo_(
 
      candPick_(
 
        assessment,
 
        ['refNo', 'ref_no', 'Ref #']
 
      )
 
    );
 
 
 
  const candidateName =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'candidate_name',
 
          'candidateName',
 
          'name',
 
          'Candidate Name'
 
        ]
 
      )
 
    );
 
 
 
  if (!refNo) {
 
    throw new Error('Ref # is required.');
 
  }
 
 
 
  if (!candidateName) {
 
    throw new Error(
 
      'Candidate Name is required.'
 
    );
 
  }
 
 
 
  const phone =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'mobile_phone_number',
 
          'phone',
 
          'mobile',
 
          'mobile_phone',
 
          'phone_number',
 
          'Mobile / Phone Number'
 
        ]
 
      )
 
    );
 
 
 
  const email =
 
    candNormalizeEmail_(
 
      candPick_(
 
        assessment,
 
        [
 
          'email_id',
 
          'email',
 
          'Email ID'
 
        ]
 
      )
 
    );
 
 
 
  const currentDesignation =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'current_designation',
 
          'currentRole',
 
          'current_role',
 
          'Current Designation'
 
        ]
 
      )
 
    );
 
 
 
  const currentCompany =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'current_company',
 
          'currentCompany',
 
          'Current Company'
 
        ]
 
      )
 
    );
 
 
 
  const location =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'location',
 
          'Location'
 
        ]
 
      )
 
    );
 
 
 
  const totalExperience =
 
    candNumberOrBlank_(
 
      candPick_(
 
        assessment,
 
        [
 
          'total_experience_years',
 
          'total_experience',
 
          'totalExperience',
 
          'Total Experience (Yrs)'
 
        ]
 
      )
 
    );
 
 
 
  const relevantExperience =
 
    candNumberOrBlank_(
 
      candPick_(
 
        assessment,
 
        [
 
          'relevant_industry_experience_years',
 
          'relevant_industry_experience',
 
          'relevantIndustryExperience',
 
          'Relevant Industry Experience (Yrs)'
 
        ]
 
      )
 
    );
 
 
 
  const evidenceStrength =
 
    candNumberOrBlank_(
 
      candPick_(
 
        assessment,
 
        [
 
          'evidence_strength_10',
 
          'evidence_strength',
 
          'evidenceStrength',
 
          'Evidence Strength /10'
 
        ]
 
      )
 
    );
 
 
 
  const atsMatchNumber =
 
    candPercentNumber_(
 
      candPick_(
 
        assessment,
 
        [
 
          'ats_match_percent',
 
          'ats_match',
 
          'atsMatch',
 
          'ATS Match %'
 
        ]
 
      )
 
    );
 
 
 
  const mustHaveFit =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'must_have_fit',
 
          'mustHaveFit',
 
          'Must-Have Fit'
 
        ]
 
      )
 
    );
 
 
 
  const coreRoleFit =
 
    candNumberOrBlank_(
 
      candPick_(
 
        assessment,
 
        [
 
          'core_role_fit_10',
 
          'core_role_fit',
 
          'coreRoleFit',
 
          'Core Role Fit /10'
 
        ]
 
      )
 
    );
 
 
 
  const keyStrengths =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'key_strengths',
 
          'keyStrengths',
 
          'Key Strengths'
 
        ]
 
      )
 
    );
 
 
 
  const keyGap =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'key_gap_risk',
 
          'key_gap',
 
          'keyGap',
 
          'Key Gap / Risk'
 
        ]
 
      )
 
    );
 
 
 
  const executiveAssessment =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'executive_assessment',
 
          'executiveAssessment',
 
          'Executive Assessment'
 
        ]
 
      )
 
    );
 
 
 
  const decision =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'decision',
 
          'Decision'
 
        ]
 
      )
 
    );
 
 
 
  let recruitmentStage =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'recruitment_stage',
 
          'recruitmentStage',
 
          'Recruitment Stage'
 
        ]
 
      )
 
    );
 
 
 
  if (!recruitmentStage) {
 
    recruitmentStage =
 
      candDefaultStageFromDecision_(
 
        decision
 
      );
 
  }
 
 
 
  let interviewStatus =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'interview_status',
 
          'interviewStatus',
 
          'Interview Status'
 
        ]
 
      )
 
    );
 
 
 
  let clientStatus =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'client_status',
 
          'clientStatus',
 
          'Client Status'
 
        ]
 
      )
 
    );
 
const lifecycleSync =
 
  candResolveLifecycleSync_(
 
    recruitmentStage,
 
    interviewStatus,
 
    clientStatus
 
  );
 
 
 
 
 
recruitmentStage =
 
  lifecycleSync.recruitmentStage;
 
 
 
 
 
interviewStatus =
 
  lifecycleSync.interviewStatus;
 
 
 
 
 
clientStatus =
 
  lifecycleSync.clientStatus;
 
 
 
 
 
 
 
 
 
  const expectedSalary =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'expected_salary',
 
          'expectedSalary',
 
          'Expected Salary'
 
        ]
 
      )
 
    );
 
 
 
  const availability =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'availability',
 
          'Availability'
 
        ]
 
      )
 
    );
 
 
 
  const recruiterNotes =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'recruiter_notes',
 
          'recruiterNotes',
 
          'Recruiter Notes'
 
        ]
 
      )
 
    );
 
 
 
  let cvFileLink =
 
  candText_(
 
    candPick_(
 
      assessment,
 
      [
 
        'cv_file_link',
 
        'cvFileLink',
 
        'cv_file_name',
 
        'fileName',
 
        'CV / File Link'
 
      ]
 
    )
 
  );
 
 
 
  const dateScreened =
 
    candText_(
 
      candPick_(
 
        assessment,
 
        [
 
          'date_screened',
 
          'dateScreened',
 
          'Date Screened'
 
        ]
 
      )
 
    ) ||
 
    today;
 
 
 
  const talentProfile =
 
    candNormalizeTalentProfile_(
 
      assessment.talent_profile || {}
 
    );
 
 
 
  let talentMatch =
 
    candFindTalentPerson_(
 
      email,
 
      phone,
 
      candidateName
 
    );
 
 
 
  let candidateId;
 
 
 
  if (talentMatch) {
 
    candidateId =
 
      candText_(
 
        talentMatch.object[
 
          'Candidate ID'
 
        ]
 
      );
 
  }
 
  else {
 
    candidateId =
 
      candNextCandidateId_();
 
 
 
    candAppendObjectRow_(
 
      CONFIG.SHEETS.TALENT_POOL,
 
      {
 
        'Candidate ID':
 
          candidateId,
 
 
 
        'Candidate Name':
 
          candidateName,
 
 
 
        'Mobile / Phone Number':
 
          phone,
 
 
 
        'Email ID':
 
          email,
 
 
 
        'Location':
 
          location,
 
 
 
        'Current / Latest Designation':
 
          currentDesignation,
 
 
 
        'Current / Latest Company':
 
          currentCompany,
 
 
 
        'Total Experience (Yrs)':
 
          totalExperience,
 
 
 
        'Primary Function':
 
          talentProfile.primary_function,
 
 
 
        'Secondary Function / Specialization':
 
          talentProfile.secondary_function_specialization,
 
 
 
        'Industries / Domains':
 
          talentProfile.industries_domains,
 
 
 
        'Core Skills':
 
          talentProfile.core_skills,
 
 
 
        'Seniority Level':
 
          talentProfile.seniority_level,
 
 
 
        'Systems / Tools':
 
          talentProfile.systems_tools,
 
 
 
        'Geographic / Market Exposure':
 
          talentProfile.geographic_market_exposure,
 
 
 
        'Career Profile Summary':
 
          talentProfile.career_profile_summary,
 
 
 
        'Talent Pool Status':
 
          'Active',
 
 
 
        'First Screened Date':
 
          today,
 
 
 
        'Last Assessed Date':
 
          today,
 
 
 
        'Ref #s Assessed Against':
 
          '',
 
 
 
        'Assessment Count':
 
          0,
 
 
 
        'Latest Decision':
 
          '',
 
 
 
        'Latest Recruitment Stage':
 
          '',
 
 
 
        'Source CV / File':
 
          cvFileLink,
 
 
 
        'Talent Pool Notes':
 
          '',
 
 
 
        'Last Updated':
 
          now
 
 
        ,
 
        'Professional Summary':
          talentProfile.professional_summary,
 
        'Employment / Career History':
          talentProfile.employment_career_history,
 
        'Education':
          talentProfile.education
      }
 
    );
 
 
 
    talentMatch =
 
      candFindTalentByCandidateId_(
 
        candidateId
 
      );
 
  }
 
 
 
  if (!candidateId) {
 
    throw new Error(
 
      'Could not determine Candidate ID.'
 
    );
 
  }
 
  /*
 
   * Preserve the original candidate CV only when
 
   * an actual CV payload was supplied by the browser.
 
   *
 
   * Candidate ID is already known at this point,
 
   * so the permanent Drive filename can use it.
 
   */
 
    const existingMasterCvLink =
 
    talentMatch
 
      ? candText_(
 
          talentMatch.object[
 
            'Source CV / File'
 
          ]
 
        )
 
      : '';
 
 
 
  if (existingMasterCvLink) {
 
 
 
    /*
 
     * Existing candidate already has a master CV.
 
     * Preserve that permanent source file.
 
     */
 
    cvFileLink =
 
      existingMasterCvLink;
 
 
 
  }
 
  else if (
 
    cvUpload &&
 
    cvUpload.fileBase64
 
  ) {
 
 
 
    /*
 
     * No permanent master CV exists yet.
 
     * Preserve the currently uploaded CV in Drive.
 
     */
 
    cvFileLink =
 
      candSaveCandidateCv_(
 
        candidateId,
 
        candidateName,
 
        cvUpload
 
      );
 
 
 
  }
 
  else {
 
 
 
    /*
 
     * A new/master CV cannot be preserved because
 
     * the browser no longer has the original file.
 
     */
 
    throw new Error(
 
      'The original candidate CV is no longer available. Please select and assess the CV again before saving to ATS.'
 
    );
 
 
 
  }
 
  const criterionResults =
 
    Array.isArray(assessment.criterion_results)
 
      ? assessment.criterion_results
 
      : (Array.isArray(assessment.criterionResults) ? assessment.criterionResults : []);
 
 
 
  const criterionResultsJson =
 
    criterionResults.length
 
      ? JSON.stringify(criterionResults)
 
      : '';
 
 
 
  const candidateRow = {
 
    'Candidate ID':
 
      candidateId,
 
 
 
    'Ref #':
 
      refNo,
 
 
 
    'Candidate Name':
 
      candidateName,
 
 
 
    'Mobile / Phone Number':
 
      phone,
 
 
 
    'Email ID':
 
      email,
 
 
 
    'Current Designation':
 
      currentDesignation,
 
 
 
    'Current Company':
 
      currentCompany,
 
 
 
    'Location':
 
      location,
 
 
 
    'Total Experience (Yrs)':
 
      totalExperience,
 
 
 
    'Relevant Industry Experience (Yrs)':
 
      relevantExperience,
 
 
 
    'Date Screened':
 
      dateScreened,
 
 
 
    'Evidence Strength /10':
 
      evidenceStrength,
 
 
 
    'ATS Match %':
 
      candFormatPercentForSheet_(
 
        atsMatchNumber
 
      ),
 
 
 
    'Must-Have Fit':
 
      mustHaveFit,
 
 
 
    'Core Role Fit /10':
 
      coreRoleFit,
 
 
 
    'Key Strengths':
 
      keyStrengths,
 
 
 
    'Key Gap / Risk':
 
      keyGap,
 
 
 
    'Executive Assessment':
 
      executiveAssessment,
 
 
 
    'Decision':
 
      decision,
 
 
 
    'Recruitment Stage':
 
      recruitmentStage,
 
 
 
    'Interview Status':
 
      interviewStatus,
 
 
 
    'Client Status':
 
      clientStatus,
 
 
 
    'Expected Salary':
 
      expectedSalary,
 
 
 
    'Availability':
 
      availability,
 
 
 
    'Recruiter Notes':
 
      recruiterNotes,
 
 
 
    'CV / File Link':
 
      cvFileLink,
 
 
 
    'Last Updated':
 
      now,
 
 
 
    'Criterion Results JSON':
 
      criterionResultsJson
 
  };
 
 
 
  const existingAssessment =
 
    candFindCandidateAssessment_(
 
      candidateId,
 
      refNo
 
    );
 
  /*
 
   * PRESERVE EXISTING RECRUITER LIFECYCLE
 
   * -------------------------------------
 
   * Re-assessing the SAME Candidate + Ref #
 
   * must not reset recruiter-progressed
 
   * operational statuses back to the AI default.
 
   *
 
   * AI Decision / scoring may be refreshed.
 
   * Recruitment Stage, Interview Status and
 
   * Client Status remain recruiter-controlled
 
   * once the assessment record already exists.
 
   */
 
 
 
  if (existingAssessment) {
 
 
 
    const existingLifecycle =
 
      existingAssessment.object;
 
 
 
 
 
    candidateRow[
 
      'Recruitment Stage'
 
    ] =
 
      candText_(
 
        existingLifecycle[
 
          'Recruitment Stage'
 
        ]
 
      ) ||
 
      recruitmentStage;
 
 
 
 
 
    candidateRow[
 
      'Interview Status'
 
    ] =
 
      candText_(
 
        existingLifecycle[
 
          'Interview Status'
 
        ]
 
      );
 
 
 
 
 
    candidateRow[
 
      'Client Status'
 
    ] =
 
      candText_(
 
        existingLifecycle[
 
          'Client Status'
 
        ]
 
      );
 
 
 
  }
 
  let action;
 
 
 
  if (existingAssessment) {
 
    candUpdateRowByNumber_(
 
      CONFIG.SHEETS.CANDIDATES,
 
      existingAssessment.rowNumber,
 
      candidateRow
 
    );
 
 
 
    action = 'updated';
 
  }
 
  else {
 
    candAppendObjectRow_(
 
      CONFIG.SHEETS.CANDIDATES,
 
      candidateRow
 
    );
 
 
 
    action = 'created';
 
  }
 
 
 
  candUpdateTalentPool_(
 
    candidateId,
 
    {
 
      refNo:
 
        refNo,
 
 
 
      candidateName:
 
        candidateName,
 
 
 
      phone:
 
        phone,
 
 
 
      email:
 
        email,
 
 
 
      location:
 
        location,
 
 
 
      currentDesignation:
 
        currentDesignation,
 
 
 
      currentCompany:
 
        currentCompany,
 
 
 
      totalExperience:
 
        totalExperience,
 
 
 
      decision:
 
        decision,
 
 
 
      recruitmentStage:
 
        recruitmentStage,
 
 
 
      cvFileLink:
 
        cvFileLink,
 
 
 
      talentProfile:
 
        talentProfile,
 
 
 
      today:
 
        today,
 
 
 
      now:
 
        now
 
    }
 
  );
 
 
 
  SpreadsheetApp.flush();
 
 
 
  return {
 
    success:
 
      true,
 
 
 
    action:
 
      action,
 
 
 
    candidateId:
 
      candidateId,
 
 
 
    refNo:
 
      refNo,
 
 
 
    candidateName:
 
      candidateName,
 
 
 
    message:
 
      action === 'updated'
 
        ? 'Existing candidate assessment updated in the live ATS.'
 
        : 'Candidate assessment saved to the live ATS.'
 
  };
 
}
 
 
 
/* =========================================================
 
   RECRUITER LIFECYCLE UPDATE
 
   ========================================================= */
 
 
 
function updateCandidateLifecycle(
 
  sessionToken,
 
  payload
 
) {
 
 
 
  requireATSOperationalUser_(
 
    sessionToken
 
  );
 
 
 
 
 
  if (!payload) {
 
    throw new Error(
 
      'Missing lifecycle update payload.'
 
    );
 
  }
 
 
 
 
 
  const candidateId =
 
    candText_(
 
      payload.candidateId
 
    );
 
 
 
 
 
  const refNo =
 
    candNormalizeRefNo_(
 
      payload.refNo
 
    );
 
 
 
 
 
  if (!candidateId) {
 
    throw new Error(
 
      'Candidate ID is required.'
 
    );
 
  }
 
 
 
 
 
  if (!refNo) {
 
    throw new Error(
 
      'Ref # is required.'
 
    );
 
  }
 
 
 
 
 
  const existingAssessment =
 
    candFindCandidateAssessment_(
 
      candidateId,
 
      refNo
 
    );
 
 
 
 
 
  if (!existingAssessment) {
 
    throw new Error(
 
      'Candidate assessment not found for ' +
 
      candidateId +
 
      ' / Ref #' +
 
      refNo +
 
      '.'
 
    );
 
  }
 
 
 
 
 
  const current =
 
    existingAssessment.object;
 
 
 
 
 
  /*
 
   * Start from the CURRENT live values.
 
   * Only fields explicitly supplied by the recruiter
 
   * are replaced.
 
   */
 
 
 
  let recruitmentStage =
 
    candText_(
 
      current[
 
        'Recruitment Stage'
 
      ]
 
    );
 
 
 
 
 
  let interviewStatus =
 
    candText_(
 
      current[
 
        'Interview Status'
 
      ]
 
    );
 
 
 
 
 
  let clientStatus =
 
    candText_(
 
      current[
 
        'Client Status'
 
      ]
 
    );
 
 
 
 
 
  if (
 
    Object.prototype.hasOwnProperty.call(
 
      payload,
 
      'recruitmentStage'
 
    )
 
  ) {
 
 
 
    recruitmentStage =
 
      candText_(
 
        payload.recruitmentStage
 
      );
 
 
 
  }
 
 
 
 
 
  if (
 
    Object.prototype.hasOwnProperty.call(
 
      payload,
 
      'interviewStatus'
 
    )
 
  ) {
 
 
 
    interviewStatus =
 
      candText_(
 
        payload.interviewStatus
 
      );
 
 
 
  }
 
 
 
 
 
  if (
 
    Object.prototype.hasOwnProperty.call(
 
      payload,
 
      'clientStatus'
 
    )
 
  ) {
 
 
 
    clientStatus =
 
      candText_(
 
        payload.clientStatus
 
      );
 
 
 
  }
 
 
 
 
 
  /*
 
   * Apply central T -> U -> V synchronization.
 
   */
 
 
 
  const lifecycleSync =
 
    candResolveLifecycleSync_(
 
      recruitmentStage,
 
      interviewStatus,
 
      clientStatus
 
    );
 
 
 
 
 
  recruitmentStage =
 
    lifecycleSync.recruitmentStage;
 
 
 
 
 
  interviewStatus =
 
    lifecycleSync.interviewStatus;
 
 
 
 
 
  clientStatus =
 
    lifecycleSync.clientStatus;
 
 
 
 
 
  const now =
 
    new Date();
 
 
 
 
 
  /*
 
   * Update only lifecycle fields.
 
   * AI Decision and assessment scoring remain unchanged.
 
   */
 
 
 
  candUpdateRowByNumber_(
 
    CONFIG.SHEETS.CANDIDATES,
 
    existingAssessment.rowNumber,
 
    {
 
      'Recruitment Stage':
 
        recruitmentStage,
 
 
 
      'Interview Status':
 
        interviewStatus,
 
 
 
      'Client Status':
 
        clientStatus,
 
 
 
      'Last Updated':
 
        now
 
    }
 
  );
 
 
 
 
 
  
 
 
 
 
 
  SpreadsheetApp.flush();
 
 
 
 
 
  return {
 
 
 
    success:
 
      true,
 
 
 
    candidateId:
 
      candidateId,
 
 
 
    refNo:
 
      refNo,
 
 
 
    recruitmentStage:
 
      recruitmentStage,
 
 
 
    interviewStatus:
 
      interviewStatus,
 
 
 
    clientStatus:
 
      clientStatus,
 
 
 
    message:
 
      'Candidate lifecycle updated successfully.'
 
 
 
  };
 
 
 
}
 
 
 
 
 
 
 
/* =========================================================
 
   SHEET READ HELPERS
 
   ========================================================= */
 
 
 
function candReadSheetObjects_(
 
  sheetName
 
) {
 
  const ss =
 
    getSpreadsheet_();
 
 
 
  const sheet =
 
    ss.getSheetByName(
 
      sheetName
 
    );
 
 
 
  if (!sheet) {
 
    throw new Error(
 
      'Sheet not found: ' +
 
      sheetName
 
    );
 
  }
 
 
 
  const headers =
 
    candHeaders_(
 
      sheet
 
    );
 
 
 
  const keyIndex =
 
    headers.indexOf(
 
      'Candidate ID'
 
    );
 
 
 
  if (keyIndex < 0) {
 
    throw new Error(
 
      'Candidate ID column not found in ' +
 
      sheetName
 
    );
 
  }
 
 
 
  const firstEmptyRow =
 
    candFindFirstEmptyDataRow_(
 
      sheet,
 
      keyIndex + 1
 
    );
 
 
 
  const lastDataRow =
 
    firstEmptyRow - 1;
 
 
 
  if (lastDataRow < 2) {
 
    return [];
 
  }
 
 
 
  const rowCount =
 
    lastDataRow - 1;
 
 
 
  const values =
 
    sheet
 
      .getRange(
 
        2,
 
        1,
 
        rowCount,
 
        headers.length
 
      )
 
      .getValues();
 
 
 
  const displayValues =
 
    sheet
 
      .getRange(
 
        2,
 
        1,
 
        rowCount,
 
        headers.length
 
      )
 
      .getDisplayValues();
 
 
 
  const rows = [];
 
 
 
  for (
 
    let r = 0;
 
    r < values.length;
 
    r++
 
  ) {
 
    const object = {};
 
 
 
    for (
 
      let c = 0;
 
      c < headers.length;
 
      c++
 
    ) {
 
      const header =
 
        headers[c];
 
 
 
      if (!header) {
 
        continue;
 
      }
 
 
 
      let value =
 
        values[r][c];
 
 
 
      if (
 
        header === 'Ref #' ||
 
        header ===
 
        'Ref #s Assessed Against'
 
      ) {
 
        value =
 
          displayValues[r][c];
 
      }
 
 
 
      object[header] =
 
        value;
 
    }
 
 
 
    rows.push({
 
      rowNumber:
 
        r + 2,
 
 
 
      object:
 
        object
 
    });
 
  }
 
 
 
  return rows;
 
}
 
 
 
 
 
function candHeaders_(
 
  sheet
 
) {
 
  const lastColumn =
 
    sheet.getLastColumn();
 
 
 
  if (lastColumn < 1) {
 
    throw new Error(
 
      'No headers found in sheet: ' +
 
      sheet.getName()
 
    );
 
  }
 
 
 
  return sheet
 
    .getRange(
 
      1,
 
      1,
 
      1,
 
      lastColumn
 
    )
 
    .getDisplayValues()[0]
 
    .map(candText_);
 
}
 
 
 
 
 
function candFindFirstEmptyDataRow_(
 
  sheet,
 
  keyColumnNumber
 
) {
 
  const scanRows =
 
    Math.max(
 
      sheet.getMaxRows() - 1,
 
      1
 
    );
 
 
 
  const values =
 
    sheet
 
      .getRange(
 
        2,
 
        keyColumnNumber,
 
        scanRows,
 
        1
 
      )
 
      .getDisplayValues();
 
 
 
  for (
 
    let i = 0;
 
    i < values.length;
 
    i++
 
  ) {
 
    if (
 
      candText_(
 
        values[i][0]
 
      ) === ''
 
    ) {
 
      return i + 2;
 
    }
 
  }
 
 
 
  sheet.insertRowAfter(
 
    sheet.getMaxRows()
 
  );
 
 
 
  return sheet.getMaxRows();
 
}
 
 
 
 
 
/* =========================================================
 
   MATCHING
 
   ========================================================= */
 
 
 
function candFindTalentPerson_(
 
  email,
 
  phone,
 
  candidateName
 
) {
 
  const rows =
 
    candReadSheetObjects_(
 
      CONFIG.SHEETS.TALENT_POOL
 
    );
 
 
 
  const normalizedEmail =
 
    candNormalizeEmail_(
 
      email
 
    );
 
 
 
  const normalizedPhone =
 
    candNormalizePhone_(
 
      phone
 
    );
 
 
 
  const normalizedName =
 
    candNormalizeName_(
 
      candidateName
 
    );
 
 
 
  if (normalizedEmail) {
 
    const match =
 
      rows.find(
 
        function(row) {
 
          return (
 
            candNormalizeEmail_(
 
              row.object['Email ID']
 
            ) ===
 
            normalizedEmail
 
          );
 
        }
 
      );
 
 
 
    if (match) {
 
      return match;
 
    }
 
  }
 
 
 
  if (normalizedPhone) {
 
    const match =
 
      rows.find(
 
        function(row) {
 
          return (
 
            candNormalizePhone_(
 
              row.object[
 
                'Mobile / Phone Number'
 
              ]
 
            ) ===
 
            normalizedPhone
 
          );
 
        }
 
      );
 
 
 
    if (match) {
 
      return match;
 
    }
 
  }
 
 
 
  if (normalizedName) {
 
    const matches =
 
      rows.filter(
 
        function(row) {
 
          return (
 
            candNormalizeName_(
 
              row.object[
 
                'Candidate Name'
 
              ]
 
            ) ===
 
            normalizedName
 
          );
 
        }
 
      );
 
 
 
    if (
 
      matches.length === 1
 
    ) {
 
      return matches[0];
 
    }
 
  }
 
 
 
  return null;
 
}
 
 
 
 
 
function candFindTalentByCandidateId_(
 
  candidateId
 
) {
 
  const rows =
 
    candReadSheetObjects_(
 
      CONFIG.SHEETS.TALENT_POOL
 
    );
 
 
 
  return rows.find(
 
    function(row) {
 
      return (
 
        candText_(
 
          row.object[
 
            'Candidate ID'
 
          ]
 
        ) ===
 
        candidateId
 
      );
 
    }
 
  ) || null;
 
}
 
 
 
 
 
function candFindCandidateAssessment_(
 
  candidateId,
 
  refNo
 
) {
 
  const rows =
 
    candReadSheetObjects_(
 
      CONFIG.SHEETS.CANDIDATES
 
    );
 
 
 
  return rows.find(
 
    function(row) {
 
      return (
 
        candText_(
 
          row.object[
 
            'Candidate ID'
 
          ]
 
        ) ===
 
        candidateId &&
 
        candNormalizeRefNo_(
 
          row.object['Ref #']
 
        ) ===
 
        refNo
 
      );
 
    }
 
  ) || null;
 
}
 
 
 
 
 
/* =========================================================
 
   WRITE HELPERS
 
   ========================================================= */
 
 
 
function candAppendObjectRow_(
 
  sheetName,
 
  object
 
) {
 
  const ss =
 
    getSpreadsheet_();
 
 
 
  const sheet =
 
    ss.getSheetByName(
 
      sheetName
 
    );
 
 
 
  if (!sheet) {
 
    throw new Error(
 
      'Sheet not found: ' +
 
      sheetName
 
    );
 
  }
 
 
 
  const headers =
 
    candHeaders_(
 
      sheet
 
    );
 
 
 
  const keyIndex =
 
    headers.indexOf(
 
      'Candidate ID'
 
    );
 
 
 
  if (keyIndex < 0) {
 
    throw new Error(
 
      'Candidate ID column not found in ' +
 
      sheetName
 
    );
 
  }
 
 
 
  const newRow =
 
    candFindFirstEmptyDataRow_(
 
      sheet,
 
      keyIndex + 1
 
    );
 
 
 
  candForceTextFormats_(
 
    sheet,
 
    headers,
 
    newRow
 
  );
 
 
 
  const values =
 
    headers.map(
 
      function(header) {
 
        if (
 
          Object.prototype
 
            .hasOwnProperty.call(
 
              object,
 
              header
 
            )
 
        ) {
 
          return object[header];
 
        }
 
 
 
        return '';
 
      }
 
    );
 
 
 
  sheet
 
    .getRange(
 
      newRow,
 
      1,
 
      1,
 
      headers.length
 
    )
 
    .setValues([
 
      values
 
    ]);
 
 
 
  return newRow;
 
}
 
 
 
 
 
function candUpdateRowByNumber_(
 
  sheetName,
 
  rowNumber,
 
  updates
 
) {
 
  const ss =
 
    getSpreadsheet_();
 
 
 
  const sheet =
 
    ss.getSheetByName(
 
      sheetName
 
    );
 
 
 
  if (!sheet) {
 
    throw new Error(
 
      'Sheet not found: ' +
 
      sheetName
 
    );
 
  }
 
 
 
  const headers =
 
    candHeaders_(
 
      sheet
 
    );
 
 
 
  const currentValues =
 
    sheet
 
      .getRange(
 
        rowNumber,
 
        1,
 
        1,
 
        headers.length
 
      )
 
      .getValues()[0];
 
 
 
  const output =
 
    headers.map(
 
      function(header, index) {
 
        if (
 
          Object.prototype
 
            .hasOwnProperty.call(
 
              updates,
 
              header
 
            )
 
        ) {
 
          return updates[header];
 
        }
 
 
 
        return currentValues[index];
 
      }
 
    );
 
 
 
  candForceTextFormats_(
 
    sheet,
 
    headers,
 
    rowNumber
 
  );
 
 
 
  sheet
 
    .getRange(
 
      rowNumber,
 
      1,
 
      1,
 
      headers.length
 
    )
 
    .setValues([
 
      output
 
    ]);
 
}
 
 
 
 
 
function candForceTextFormats_(
 
  sheet,
 
  headers,
 
  rowNumber
 
) {
 
  [
 
    'Ref #',
 
    'Ref #s Assessed Against'
 
  ].forEach(
 
    function(headerName) {
 
      const index =
 
        headers.indexOf(
 
          headerName
 
        );
 
 
 
      if (index >= 0) {
 
        sheet
 
          .getRange(
 
            rowNumber,
 
            index + 1
 
          )
 
          .setNumberFormat('@');
 
      }
 
    }
 
  );
 
}
 
 
 
 
 
/* =========================================================
 
   TALENT POOL UPDATE
 
   ========================================================= */
 
 
 
function candUpdateTalentPool_(
 
  candidateId,
 
  data
 
) {
 
  const match =
 
    candFindTalentByCandidateId_(
 
      candidateId
 
    );
 
 
 
  if (!match) {
 
    throw new Error(
 
      'Talent Pool record not found for ' +
 
      candidateId
 
    );
 
  }
 
 
 
  const current =
 
    match.object;
 
 
 
  const refs =
 
    candParseRefList_(
 
      current[
 
        'Ref #s Assessed Against'
 
      ]
 
    );
 
 
 
  const hadRefAlready =
 
    refs.indexOf(
 
      data.refNo
 
    ) >= 0;
 
 
 
  if (!hadRefAlready) {
 
    refs.push(
 
      data.refNo
 
    );
 
  }
 
 
 
  let assessmentCount =
 
    Number(
 
      current[
 
        'Assessment Count'
 
      ] || 0
 
    );
 
 
 
  if (!hadRefAlready) {
 
    assessmentCount += 1;
 
  }
 
 
 
  if (
 
    !assessmentCount ||
 
    assessmentCount <
 
      refs.length
 
  ) {
 
    assessmentCount =
 
      refs.length;
 
  }
 
 
 
  const p =
 
    data.talentProfile || {};
 
 
 
  candUpdateRowByNumber_(
 
    CONFIG.SHEETS.TALENT_POOL,
 
    match.rowNumber,
 
    {
 
      'Candidate Name':
 
        data.candidateName ||
 
        current[
 
          'Candidate Name'
 
        ],
 
 
 
      'Mobile / Phone Number':
 
        data.phone ||
 
        current[
 
          'Mobile / Phone Number'
 
        ],
 
 
 
      'Email ID':
 
        data.email ||
 
        current['Email ID'],
 
 
 
      'Location':
 
        data.location ||
 
        current['Location'],
 
 
 
      'Current / Latest Designation':
 
        data.currentDesignation ||
 
        current[
 
          'Current / Latest Designation'
 
        ],
 
 
 
      'Current / Latest Company':
 
        data.currentCompany ||
 
        current[
 
          'Current / Latest Company'
 
        ],
 
 
 
      'Total Experience (Yrs)':
 
        data.totalExperience !== ''
 
          ? data.totalExperience
 
          : current[
 
              'Total Experience (Yrs)'
 
            ],
 
 
 
      'Primary Function':
 
        candPreferNewProfileValue_(
 
          current[
 
            'Primary Function'
 
          ],
 
          p.primary_function
 
        ),
 
 
 
      'Secondary Function / Specialization':
 
        candPreferNewProfileValue_(
 
          current[
 
            'Secondary Function / Specialization'
 
          ],
 
          p.secondary_function_specialization
 
        ),
 
 
 
      'Industries / Domains':
 
        candPreferNewProfileValue_(
 
          current[
 
            'Industries / Domains'
 
          ],
 
          p.industries_domains
 
        ),
 
 
 
      'Core Skills':
 
        candPreferNewProfileValue_(
 
          current[
 
            'Core Skills'
 
          ],
 
          p.core_skills
 
        ),
 
 
 
      'Seniority Level':
 
        candNormalizeSeniorityLevel_(
 
          p.seniority_level ||
 
          current[
 
            'Seniority Level'
 
          ],
 
          data.currentDesignation
 
        ),
 
 
 
      'Systems / Tools':
 
        candPreferNewProfileValue_(
 
          current[
 
            'Systems / Tools'
 
          ],
 
          p.systems_tools
 
        ),
 
 
 
      'Geographic / Market Exposure':
 
        candPreferNewProfileValue_(
 
          current[
 
            'Geographic / Market Exposure'
 
          ],
 
          p.geographic_market_exposure
 
        ),
 
 
 
      'Career Profile Summary':
 
        candPreferNewProfileValue_(
 
          current[
 
            'Career Profile Summary'
 
          ],
 
          p.career_profile_summary
 
        ),
 
      /*
       * Permanent structured CV profile.
       * Populate from the first CV assessment, then preserve it.
       */
      'Professional Summary':
        candPreferExistingProfileValue_(
          current[
            'Professional Summary'
          ],
          p.professional_summary
        ),
 
      'Employment / Career History':
        candPreferExistingProfileValue_(
          current[
            'Employment / Career History'
          ],
          p.employment_career_history
        ),
 
      'Education':
        candPreferExistingProfileValue_(
          current[
            'Education'
          ],
          p.education
        ),
 
 
 
      'Talent Pool Status':
 
        candText_(
 
          current[
 
            'Talent Pool Status'
 
          ]
 
        ) || 'Active',
 
 
 
      'Last Assessed Date':
 
        data.today,
 
 
 
      'Ref #s Assessed Against':
 
        refs.join(', '),
 
 
 
      'Assessment Count':
 
        assessmentCount,
 
 
 
      'Latest Decision':
 
        data.decision,
 
 
 
      'Latest Recruitment Stage':
 
        data.recruitmentStage,
 
 
 
      'Source CV / File':
 
        data.cvFileLink ||
 
        current[
 
          'Source CV / File'
 
        ],
 
 
 
      'Talent Pool Notes':
 
  candBuildTalentPoolNote_(
 
    data.refNo,
 
    data.decision,
 
    p
 
  ),
 
 
 
'Last Updated':
 
  data.now
 
    }
 
  );
 
}
 
 
 
 
 
function candPreferNewProfileValue_(
 
  currentValue,
 
  newValue
 
) {
 
  const current =
 
    candText_(
 
      currentValue
 
    );
 
 
 
  const incoming =
 
    candText_(
 
      newValue
 
    );
 
 
 
  if (incoming) {
 
    return incoming;
 
  }
 
 
 
  return current;
 
}
 
 
 
 
 
function candPreferExistingProfileValue_(
 
  currentValue,
 
  newValue
 
) {
 
  const current =
 
    candText_(
 
      currentValue
 
    );
 
  if (current) {
 
    return current;
 
  }
 
  return candText_(
 
    newValue
 
  );
 
}
 
 
function candNormalizeTalentProfile_(
 
  profile
 
) {
 
  return {
 
    primary_function:
 
      candText_(
 
        profile.primary_function
 
      ),
 
 
 
    secondary_function_specialization:
 
      candText_(
 
        profile.secondary_function_specialization
 
      ),
 
 
 
    industries_domains:
 
      candText_(
 
        profile.industries_domains
 
      ),
 
 
 
    core_skills:
 
      candText_(
 
        profile.core_skills
 
      ),
 
 
 
    seniority_level:
 
      candNormalizeSeniorityLevel_(
 
        profile.seniority_level,
 
        ''
 
      ),
 
 
 
    systems_tools:
 
      candText_(
 
        profile.systems_tools
 
      ),
 
 
 
    geographic_market_exposure:
 
      candText_(
 
        profile.geographic_market_exposure
 
      ),
 
 
 
    career_profile_summary:
 
      candText_(
 
        profile.career_profile_summary
 
      ),
 
    professional_summary:
      candText_(
        profile.professional_summary
      ),
 
    employment_career_history:
      candSerializeStructuredProfile_(profile.employment_career_history),
 
    education:
      candSerializeStructuredProfile_(profile.education)
 
 
  };
 
}
 
 
 
 
 
/* =========================================================
 
   SENIORITY LEVEL CONTROLLED-VALUE NORMALIZATION
 
   ========================================================= */
 
 
 
function candSerializeStructuredProfile_(value) {
  if (Array.isArray(value)) {
    return value.length ? JSON.stringify(value) : '';
  }
  if (value && typeof value === 'object') {
    return JSON.stringify(value);
  }
  return candText_(value);
}
 
function candNormalizeSeniorityLevel_(
 
  value,
 
  designation
 
) {
 
  const allowed = [
 
    'Individual Contributor',
 
    'Senior Professional',
 
    'Team Lead / Supervisor',
 
    'Manager',
 
    'Senior Manager',
 
    'Head / Director',
 
    'Executive / GM',
 
    'Founder / Owner',
 
    'Officer / Command'
 
  ];
 
 
 
  const raw =
 
    candText_(value);
 
 
 
  const title =
 
    candText_(designation);
 
 
 
  // Exact allowed value already returned.
 
  for (
 
    let i = 0;
 
    i < allowed.length;
 
    i++
 
  ) {
 
    if (
 
      raw.toLowerCase() ===
 
      allowed[i].toLowerCase()
 
    ) {
 
      return allowed[i];
 
    }
 
  }
 
 
 
  const combined =
 
    (
 
      raw +
 
      ' ' +
 
      title
 
    )
 
      .toLowerCase()
 
      .trim();
 
 
 
  if (
 
    /\bfounder\b|\bowner\b/.test(
 
      combined
 
    )
 
  ) {
 
    return 'Founder / Owner';
 
  }
 
 
 
  if (
 
    /\bcolonel\b|\bmajor\b|\bcaptain\b|\blieutenant\b|\bbrigadier\b|\bofficer\b|\bcommand\b/.test(
 
      combined
 
    )
 
  ) {
 
    return 'Officer / Command';
 
  }
 
 
 
  if (
 
    /\bchief\b|\bceo\b|\bcoo\b|\bcfo\b|\bcto\b|\bcmo\b|\bgeneral manager\b|\bgm\b|\bmanaging director\b/.test(
 
      combined
 
    )
 
  ) {
 
    return 'Executive / GM';
 
  }
 
 
 
  if (
 
    /\bhead\b|\bdirector\b/.test(
 
      combined
 
    )
 
  ) {
 
    return 'Head / Director';
 
  }
 
 
 
  if (
 
    /\bsenior manager\b|\bsr\.?\s*manager\b/.test(
 
      combined
 
    )
 
  ) {
 
    return 'Senior Manager';
 
  }
 
 
 
  if (
 
    /\bmanager\b|\bassistant manager\b|\bdeputy manager\b/.test(
 
      combined
 
    )
 
  ) {
 
    return 'Manager';
 
  }
 
 
 
  if (
 
    /\bteam lead\b|\bteam leader\b|\bsupervisor\b|\blead\b/.test(
 
      combined
 
    )
 
  ) {
 
    return 'Team Lead / Supervisor';
 
  }
 
 
 
  if (
 
    /\bsenior\b|\bspecialist\b|\bconsultant\b|\bengineer\b|\bexecutive\b/.test(
 
      combined
 
    )
 
  ) {
 
    return 'Senior Professional';
 
  }
 
 
 
  return 'Individual Contributor';
 
}
 
 
 
function candBuildTalentPoolNote_(
 
  refNo,
 
  decision,
 
  profile
 
) {
 
  const p =
 
    profile || {};
 
 
 
  let focus =
 
    candText_(
 
      p.primary_function
 
    );
 
 
 
  if (!focus) {
 
    focus =
 
      candText_(
 
        p.secondary_function_specialization
 
      );
 
  }
 
 
 
  if (!focus) {
 
    focus =
 
      candText_(
 
        p.industries_domains
 
      );
 
  }
 
 
 
  if (!focus) {
 
    focus =
 
      'relevant future';
 
  }
 
 
 
  const normalizedDecision =
 
    candText_(
 
      decision
 
    );
 
 
 
  if (
 
    normalizedDecision ===
 
    'Reject'
 
  ) {
 
    return (
 
      'Rejected for Ref #' +
 
      refNo +
 
      ' only; retain for future ' +
 
      focus +
 
      ' roles.'
 
    );
 
  }
 
 
 
  if (
 
    normalizedDecision ===
 
    'Hold'
 
  ) {
 
    return (
 
      'On hold for Ref #' +
 
      refNo +
 
      '; retain for future ' +
 
      focus +
 
      ' roles.'
 
    );
 
  }
 
 
 
  if (
 
    normalizedDecision ===
 
    'Priority Shortlist' ||
 
    normalizedDecision ===
 
    'Shortlist'
 
  ) {
 
    return (
 
      normalizedDecision +
 
      ' for Ref #' +
 
      refNo +
 
      '. Retain for future ' +
 
      focus +
 
      ' roles.'
 
    );
 
  }
 
 
 
  return (
 
    (
 
      normalizedDecision ||
 
      'Assessed'
 
    ) +
 
    ' for Ref #' +
 
    refNo +
 
    '. Retain for future ' +
 
    focus +
 
    ' roles.'
 
  );
 
}
 
/* =========================================================
 
   ID
 
   ========================================================= */
 
 
 
function candNextCandidateId_() {
 
  const rows =
 
    candReadSheetObjects_(
 
      CONFIG.SHEETS.TALENT_POOL
 
    );
 
 
 
  let maxId = 0;
 
 
 
  rows.forEach(
 
    function(row) {
 
      const raw =
 
        candText_(
 
          row.object[
 
            'Candidate ID'
 
          ]
 
        );
 
 
 
      const numeric =
 
        parseInt(
 
          raw.replace(
 
            /[^\d]/g,
 
            ''
 
          ),
 
          10
 
        );
 
 
 
      if (
 
        !isNaN(numeric) &&
 
        numeric > maxId
 
      ) {
 
        maxId =
 
          numeric;
 
      }
 
    }
 
  );
 
 
 
  return (
 
    'CAND-' +
 
    String(
 
      maxId + 1
 
    ).padStart(
 
      3,
 
      '0'
 
    )
 
  );
 
}
 
 
 
 
 
/* =========================================================
 
   NORMALIZATION
 
   ========================================================= */
 
 
 
function candPick_(
 
  object,
 
  keys
 
) {
 
  for (
 
    let i = 0;
 
    i < keys.length;
 
    i++
 
  ) {
 
    const key =
 
      keys[i];
 
 
 
    if (
 
      Object.prototype
 
        .hasOwnProperty.call(
 
          object,
 
          key
 
        ) &&
 
      object[key] !==
 
        undefined &&
 
      object[key] !==
 
        null
 
    ) {
 
      return object[key];
 
    }
 
  }
 
 
 
  return '';
 
}
 
 
 
 
 
function candText_(
 
  value
 
) {
 
  if (
 
    value === null ||
 
    value === undefined
 
  ) {
 
    return '';
 
  }
 
 
 
  return String(value)
 
    .trim();
 
}
 
 
 
 
 
function candNormalizeRefNo_(
 
  value
 
) {
 
  const raw =
 
    candText_(
 
      value
 
    ).replace(
 
      /^'+/,
 
      ''
 
    );
 
 
 
  if (!raw) {
 
    return '';
 
  }
 
 
 
  const digits =
 
    raw.replace(
 
      /[^\d]/g,
 
      ''
 
    );
 
 
 
  if (!digits) {
 
    return raw;
 
  }
 
 
 
  const numeric =
 
    parseInt(
 
      digits,
 
      10
 
    );
 
 
 
  if (isNaN(numeric)) {
 
    return raw;
 
  }
 
 
 
  return String(numeric)
 
    .padStart(
 
      3,
 
      '0'
 
    );
 
}
 
 
 
 
 
function candNormalizeEmail_(
 
  value
 
) {
 
  return candText_(
 
    value
 
  ).toLowerCase();
 
}
 
 
 
 
 
function candNormalizePhone_(
 
  value
 
) {
 
  const raw =
 
    candText_(
 
      value
 
    );
 
 
 
  if (!raw) {
 
    return '';
 
  }
 
 
 
  const first =
 
    raw
 
      .split('/')[0]
 
      .split(',')[0]
 
      .split(';')[0];
 
 
 
  return first.replace(
 
    /\D/g,
 
    ''
 
  );
 
}
 
 
 
 
 
function candNormalizeName_(
 
  value
 
) {
 
  return candText_(
 
    value
 
  )
 
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
 
 
 
 
 
function candNumberOrBlank_(
 
  value
 
) {
 
  if (
 
    value === null ||
 
    value === undefined ||
 
    value === ''
 
  ) {
 
    return '';
 
  }
 
 
 
  const numeric =
 
    Number(
 
      String(value)
 
        .replace(
 
          '%',
 
          ''
 
        )
 
        .trim()
 
    );
 
 
 
  return isNaN(numeric)
 
    ? ''
 
    : numeric;
 
}
 
 
 
 
 
function candPercentNumber_(
 
  value
 
) {
 
  if (
 
    value === null ||
 
    value === undefined ||
 
    value === ''
 
  ) {
 
    return '';
 
  }
 
 
 
  const numeric =
 
    Number(
 
      String(value)
 
        .replace(
 
          '%',
 
          ''
 
        )
 
        .trim()
 
    );
 
 
 
  return isNaN(numeric)
 
    ? ''
 
    : numeric;
 
}
 
 
 
 
 
function candFormatPercentForSheet_(
 
  value
 
) {
 
  if (value === '') {
 
    return '';
 
  }
 
 
 
  return (
 
    Math.round(
 
      Number(value)
 
    ) +
 
    '%'
 
  );
 
}
 
 
 
 
 
function candParseRefList_(
 
  value
 
) {
 
  const raw =
 
    candText_(
 
      value
 
    );
 
 
 
  if (!raw) {
 
    return [];
 
  }
 
 
 
  const refs = [];
 
 
 
  raw
 
    .split(',')
 
    .map(
 
      function(item) {
 
        return candNormalizeRefNo_(
 
          item
 
        );
 
      }
 
    )
 
    .filter(Boolean)
 
    .forEach(
 
      function(ref) {
 
        if (
 
          refs.indexOf(
 
            ref
 
          ) === -1
 
        ) {
 
          refs.push(
 
            ref
 
          );
 
        }
 
      }
 
    );
 
 
 
  return refs;
 
}
 
 
 
 
 
function candDefaultStageFromDecision_(
 
  decision
 
) {
 
  const normalized =
 
    candText_(
 
      decision
 
    ).toLowerCase();
 
 
 
  if (
 
    normalized ===
 
      'priority shortlist' ||
 
    normalized ===
 
      'shortlist'
 
  ) {
 
    return 'Shortlisted';
 
  }
 
 
 
  if (
 
    normalized ===
 
    'hold'
 
  ) {
 
    return 'Hold';
 
  }
 
 
 
  if (
 
    normalized ===
 
    'reject'
 
  ) {
 
    return 'Rejected';
 
  }
 
 
 
  return 'HR Screening';
 
}
 
/* =========================================================
 
   CANDIDATE LIFECYCLE SYNCHRONIZATION
 
   ========================================================= */
 
 
 
function candResolveLifecycleSync_(
 
  recruitmentStage,
 
  interviewStatus,
 
  clientStatus
 
) {
 
 
 
  let stage =
 
    candText_(
 
      recruitmentStage
 
    );
 
 
 
  let interview =
 
    candText_(
 
      interviewStatus
 
    );
 
 
 
  let client =
 
    candText_(
 
      clientStatus
 
    );
 
 
 
 
 
  const stageKey =
 
    stage.toLowerCase();
 
 
 
  const interviewKey =
 
    interview.toLowerCase();
 
 
 
  const clientKey =
 
    client.toLowerCase();
 
 
 
 
 
  /*
 
   * CLIENT STATUS HAS HIGHEST AUTHORITY
 
   * -----------------------------------
 
   * Final/downstream client outcomes automatically
 
   * synchronize Recruitment Stage.
 
   */
 
 
 
  if (
 
    clientKey === 'hired'
 
  ) {
 
 
 
    stage = 'Hired';
 
 
 
  }
 
  else if (
 
    clientKey === 'client rejected'
 
  ) {
 
 
 
    stage = 'Rejected';
 
 
 
  }
 
  else if (
 
    clientKey === 'offer'
 
  ) {
 
 
 
    stage = 'Offer Sent';
 
 
 
  }
 
    else if (
 
    clientKey === 'client interview'
 
  ) {
 
 
 
    stage = 'Client Interview';
 
 
 
    /*
 
     * A client interview request represents
 
     * a new interview round.
 
     *
 
     * If the previous interview was already
 
     * completed, reset Interview Status so the
 
     * new client interview can be scheduled.
 
     */
 
 
 
    if (
 
      interview.toLowerCase() ===
 
      'completed'
 
    ) {
 
 
 
      interview =
 
        'Not Scheduled';
 
 
 
    }
 
 
 
  }
 
 
 
 
 
  /*
 
   * INTERVIEW INITIALIZATION
 
   * ------------------------
 
   * When a candidate enters an interview stage and
 
   * no Interview Status exists yet, initialize it
 
   * as Not Scheduled.
 
   *
 
   * Existing interview statuses are preserved.
 
   */
 
 
 
  const synchronizedStageKey =
 
    stage.toLowerCase();
 
 
 
 
 
  const isInterviewStage =
 
    synchronizedStageKey ===
 
      'technical interview' ||
 
    synchronizedStageKey ===
 
      'final interview' ||
 
    synchronizedStageKey ===
 
      'client interview';
 
 
 
 
 
  if (
 
    isInterviewStage &&
 
    !interview
 
  ) {
 
 
 
    interview =
 
      'Not Scheduled';
 
 
 
  }
 
 
 
 
 
  /*
 
   * RETURN SYNCHRONIZED OPERATIONAL VALUES
 
   *
 
   * AI Decision is intentionally not included here.
 
   * It remains historical and is never rewritten by
 
   * recruiter/client lifecycle progression.
 
   */
 
 
 
  return {
 
 
 
    recruitmentStage:
 
      stage,
 
 
 
    interviewStatus:
 
      interview,
 
 
 
    clientStatus:
 
      client
 
 
 
  };
 
 
 
}
 
 
 
 
 
 
 
 
 
// PASTE THE NEW FUNCTION HERE
 
 
 
 
 
/* =========================================================
 
   CANDIDATE CV STORAGE
 
   ========================================================= */
 
 
 
function candSaveCandidateCv_(
 
  candidateId,
 
  candidateName,
 
  cvUpload
 
) {
 
 
 
  const folderId =
 
    '1dSgWd6kB7H53usGU9hK-Xn-CBbrIz8nx';
 
 
 
  const folder =
 
    DriveApp.getFolderById(
 
      folderId
 
    );
 
 
 
  const base64 =
 
    candText_(
 
      cvUpload.fileBase64
 
    );
 
 
 
  if (!base64) {
 
    throw new Error(
 
      'Candidate CV file data is missing.'
 
    );
 
  }
 
 
 
  const originalFileName =
 
    candText_(
 
      cvUpload.fileName
 
    );
 
 
 
  const mimeType =
 
    candText_(
 
      cvUpload.mimeType
 
    ) ||
 
    'application/octet-stream';
 
 
 
  let extension = '';
 
 
 
  if (
 
    originalFileName &&
 
    originalFileName.lastIndexOf('.') > -1
 
  ) {
 
    extension =
 
      originalFileName.substring(
 
        originalFileName.lastIndexOf('.')
 
      );
 
  }
 
 
 
  const safeCandidateName =
 
    candText_(
 
      candidateName
 
    )
 
      .replace(
 
        /[^a-zA-Z0-9]+/g,
 
        '-'
 
      )
 
      .replace(
 
        /^-+|-+$/g,
 
        ''
 
      ) ||
 
    'Candidate';
 
 
 
  const fileName =
 
    candidateId +
 
    '_' +
 
    safeCandidateName +
 
    '_CV' +
 
    extension;
 
 
 
  const bytes =
 
    Utilities.base64Decode(
 
      base64
 
    );
 
 
 
  const blob =
 
    Utilities.newBlob(
 
      bytes,
 
      mimeType,
 
      fileName
 
    );
 
 
 
  const existingFiles =
 
    folder.getFilesByName(
 
      fileName
 
    );
 
 
 
  if (existingFiles.hasNext()) {
 
    const existingFile =
 
      existingFiles.next();
 
 
 
    return existingFile.getUrl();
 
  }
 
 
 
  const file =
 
    folder.createFile(
 
      blob
 
    );
 
 
 
  return file.getUrl();
 
}
 
 
 
 