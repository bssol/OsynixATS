# Account chat history and inbox deletion

In Admin, open **Workspaces & Subscriptions → user account → Chat History**. This lists every support session linked to the account owner, including general support chats without a workspace or upgrade request. Filter by subject, issue, request reference or session status. Open a session in the account panel to read the conversation, see the actual sender names, open attachments, or download its transcript.

Admins and SubAdmins with **View workspaces** permission can inspect this history, including for inactive account owners. Messaging permissions are required separately to reply through Messages. Opening account history does not change the support inbox's shared read state. Sessions belonging to another account cannot be opened by changing a session ID in the account view.

The Messages page has a **Delete chat** action for support and project conversations. A confirmation explains that deletion removes the chat from the signed-in person's inbox. Deleting a support chat removes all of its currently existing sessions from that person's inbox. Another admin's or participant's inbox is unaffected. Support records, attachments, request evidence and project invitations are retained; account history remains complete. A later message or invitation update makes the relevant chat appear again.

Inbox deletion is stored per user, conversation type and conversation ID, with the latest activity timestamp at deletion. It is audited. Support session history is displayed under the account's Chat History tab rather than inside the admin Messages thread.
