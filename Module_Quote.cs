using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Rest;

using Newtonsoft.Json;

namespace MaiNi.Modules // TODO replace this namespace with yours
{
    public class Quote
    {
        public int ID { get; set; }                 //The Quote ID, which is callable from the user directly.
        public ulong AuthorID { get; set; }           //The Author's Discord ID; track this so that you don't have to look it up
        public string AuthorNickname { get; set; }  //The Author's discord nickname at the time the quote was authored.
        public string Contents { get; set; }        //Contents of the quote. Should be sanitized on add to remove @ tag
        public ulong MessageID { get; set; }             // ID of the message which created the quote; can obtain datetime from this.
        public ulong ApprovalMessageID { get; set; }        // ID of the message used to approve this quote.
        public string? Alias { get; set; }           // Allow the quote to be called by a specific name
        public ulong GuildID { get; set; } // Allow the quote to be assigned a guild ID to help make loading pending quotes easier.

        /// <summary>
        /// Creates a new quote based on the specified parameters.
        /// </summary>
        /// <param name="_QuoteID">The ID number to assign to this quote.</param>
        /// <param name="_QuoteAuthorID">The Discord ID of the quote's author.</param>
        /// <param name="_QuoteAuthorNickname">The nickname of the quote's author at the time of the quote's creation.</param>
        /// <param name="_QuoteContents">Quote's contents. Usually a discord attachment by using the "share" option for an image.</param>
        /// <param name="_MessageID">The ID of the message that requested the quote; use for timestamping.</param>
        /// <param name="_ApprovalMessageID">The ID of the message which tracks whether this quote is to be approved.</param>
        /// <param name="_QuoteAlias">Optional alias for the quote to call it by name instead of ID.</param>
        /// <param name="_GuildID">The ID of the guild with which this quote is associated.</param>
        public Quote(
            int _QuoteID = 0,
            ulong _QuoteAuthorID = 0,
            string _QuoteAuthorNickname = "nobody!",
            string _QuoteContents = "",
            ulong _MessageID = 0,
            string? _QuoteAlias = null,
            ulong _GuildID = 0)
        {
            ID = _QuoteID;
            AuthorID = _QuoteAuthorID;
            AuthorNickname = _QuoteAuthorNickname;
            GuildID = _GuildID;

            Contents = _QuoteContents;
            MessageID = _MessageID;
            if (_QuoteAlias != null) { Alias = _QuoteAlias; }
        } // End constructor Quote
    } // End class Quote

    [Group("quote")]
    [Alias("q")]
    [Summary("Meme library")]
    public class Module_Quote : ModuleBase<SocketCommandContext>
    {

        #region Properties


#pragma warning disable IDE0044 // Add readonly modifier. It's not readonly! Why is the editor telling me to make it readonly!!
        private List<Quote> QuoteListPending = new();
        private List<Quote> QuoteListMaster = new();
#pragma warning restore IDE0044 // Add readonly modifier
        private static string QuoteListMasterPath { get { return $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}Data{Path.DirectorySeparatorChar}Quote_Master.json"; } } // TODO modify this path if you want.

        private readonly DiscordSocketClient _client = new(); // Never used, not even sure how this can be made or used, can delete.
        private readonly Random RandomNumberGenerator;

        private readonly Dictionary<string, bool> EmoteDictionary = new() { // TODO replace with the quote approval reactions for your server!
            ["<:MeleeHmm:884168926823592017>"] = true,
            ["<:Masaka:906943699076915241>"] = false
        };

        #endregion Properties

        #region Structors

        public Module_Quote()
        { // Load the quotes lists from the configuration files

            // Initialize the Random() with a 'random' seed
            RandomNumberGenerator = new Random((int)DateTime.Now.Ticks);

            // Make sure the Data folder exists
            if (!Directory.Exists($"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}Data"))
            { Directory.CreateDirectory($"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}Data"); }

#pragma warning disable CS8601 // Possible null reference assignment. Shut up!!! It's not null! I made sure it wouldn't be right above!!
            // Load the master (approved) quote list
            if (!File.Exists(QuoteListMasterPath)) { File.WriteAllText(QuoteListMasterPath, "{}"); }
            QuoteListMaster = JsonConvert.DeserializeObject<List<Quote>>(File.ReadAllText(QuoteListMasterPath));
            if(QuoteListMaster == null) { QuoteListMaster = new List<Quote>(); }
#pragma warning restore CS8601 // Possible null reference assignment.

            // Had to scrap loading pending quotes from a file; couldn't figure out how to get a context to attach events to in the constructor upon loading the module initially :(
            QuoteListPending = new List<Quote>();

        } // End constructor Module_Quote

        ~Module_Quote()
        { // Upon destructing the module, write the quotes lists with all their changes over the previous files.
            WriteQuoteListsToFile();
        } // End destructor Module_Quote

        #endregion Structors

        #region Utility

        private void WriteQuoteListsToFile()
        {
            File.WriteAllText(QuoteListMasterPath, JsonConvert.SerializeObject(QuoteListMaster, Formatting.Indented));
        } // End method WriteQuoteListsToFile

        public static string QuoteConfigurationFile() // Use this method to load the configurations for the quote module for each server
        { // Verify the path exists by creating it if it doesn't before returning it.
            string path = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}Data{Path.DirectorySeparatorChar}Quotes.json";
            if (!File.Exists(path)) { File.WriteAllText(path, ""); }
            return path;
        } // End method QuoteConfigurationFile

        private SocketTextChannel GetQuoteConfigurationChannel() // TODO change to be how you want to get the approval channel id.
        { // Placeholder for however MegaMasterX wants to find this channel. I'm gonna put the ID in hardcoded for now.
            return Context.Guild.GetTextChannel(883100946966650880);
        } // End method GetQuoteApprovalChannel

        async private Task RequestQuoteApprovalAsync(SocketCommandContext Context, Quote QuoteToAdd)
        { // Send the request to the proper channel for the quote approval. 
            RestUserMessage QuoteRequestMessage = await GetQuoteConfigurationChannel().SendMessageAsync(
                $"__Quote Add Request__\n" +
                $"**Requestor**: {QuoteToAdd.AuthorNickname} ({QuoteToAdd.AuthorID})\n" +
                $"**Quote**: {QuoteToAdd.Contents}");

            // Add this message as the ApprovalMessageID. This will be used to check which quote a message is approving.
            QuoteToAdd.ApprovalMessageID = QuoteRequestMessage.Id;

            // Add the approval reactions to the message as the way to approve or deny quotes.
            // The .Select is just a one-line way to apply the "Emote.Parse" to each key in the EmoteDictionary.
            // // Basically turn each of the raw emote strings into an actual Emote class object.
            await QuoteRequestMessage.AddReactionsAsync( EmoteDictionary.Keys.Select( EachEmoteRaw => Emote.Parse(EachEmoteRaw) ) );

            // Unfortunately, there doesn't seem to be a way to add an event listener for just the request's message, so we have to lsiten for them all.
            // Only need to add a listener if we are not already listening for one. if count is 1, this quote is the only one in the list, so add the listener.
            Console.WriteLine($"QuoteListPending: {QuoteListPending.Count}");
            if (QuoteListPending.Count == 1) { Context.Client.ReactionAdded += ListenForQuoteApprovalAsync; } 

        } // End RequestQuoteApproval

        async private Task ListenForQuoteApprovalAsync(
            Cacheable<IUserMessage, ulong> MessageToWhichTheReactionWasAdded,
            Cacheable<IMessageChannel, ulong> ChannelInWhichTheMessageWhichWasReactedToIsIn,
            SocketReaction ReactionWhichWasAdded)
        { // Event listener method. Wait until either the approve or deny reactions have been added and then move the quote.

            var ApprovalConditions = new Dictionary<string, bool>()
            {
                ["MessageIsNull"] = MessageToWhichTheReactionWasAdded.Value == null, // Make sure the message isn't null/hasn't been deleted.
                ["IsPending"] = !QuoteListPending.Any(EachQuote => EachQuote.ApprovalMessageID == MessageToWhichTheReactionWasAdded.Id), // Make sure the message is associated with a pending quote.
                ["ValidEmote"] = !EmoteDictionary.ContainsKey(ReactionWhichWasAdded.Emote.ToString()), // Make sure the emote used is one of the valid options.
                ["UserIsNotMe"] = ReactionWhichWasAdded.UserId == Context.Client.CurrentUser.Id // Make sure the reaction wasn't added by this bot.
            };

            // Debug
            /*
            Console.WriteLine("----");
            foreach (var cond in ApprovalConditions) { Console.WriteLine($"{cond.Key} : {cond.Value}"); }
            Console.WriteLine($"AprovalConditions: {ApprovalConditions.Values.Any()}");
            */

            if (ApprovalConditions.Values.Any(Condition => Condition)){ return; } // If any of these conditions are not fulfilled, just return.

            try
            {
                // Get the Quote associated with this approval message's id
                var ApprovedOrDeniedQuote = QuoteListPending.Find(
                    EachQuoteInThisList => EachQuoteInThisList.ApprovalMessageID == MessageToWhichTheReactionWasAdded.Id);

                //  We know the quote has been approved or denied (reaction was in the EmoteDictionary) and that the quote is in the pending list. We remove it regardless of whether its approved or denied.
                QuoteListPending.Remove(ApprovedOrDeniedQuote);

                // Use the dictionary to simply parse whether the emote was approved or denied
                if (EmoteDictionary[ReactionWhichWasAdded.Emote.ToString()])
                { // If the reaction which was added was the approve emote, this evaluates to true

                    QuoteListMaster.Add(ApprovedOrDeniedQuote);  // Add the quote! :D
                    await GetQuoteConfigurationChannel().SendMessageAsync($"A new quote is born! ID = {ApprovedOrDeniedQuote.ID}");
                    WriteQuoteListsToFile(); // Write the update to the quote master list.

                } // End if

                else if (!EmoteDictionary[ReactionWhichWasAdded.Emote.ToString()])
                { // If the reaction which was added was the deny emote, this evaluates to false

                    await GetQuoteConfigurationChannel().SendMessageAsync($"The quote wwas denied. ID = {ApprovedOrDeniedQuote.ID}");

                } // End else if

            } // End Try

            catch (Exception e) { Console.WriteLine(e.Message); } // Just in case 

            // If there are no more pending quotes, remove the listener.
            finally { if (QuoteListPending.Count == 0) { Context.Client.ReactionAdded -= ListenForQuoteApprovalAsync; } }

        } // End method ListenForQuoteApproval

        #endregion Utility

        #region Commands

        [Command]
        [Summary("Return a random quote from the library! Default for argument-absent calls.")]
        public async Task QuoteRandomAsync()
        {
            Quote QuoteToQuote = QuoteListMaster[RandomNumberGenerator.Next(0, QuoteListMaster.Count)];
            await ReplyAsync($"Quote {QuoteToQuote.ID} by {QuoteToQuote.AuthorNickname}:\n{QuoteToQuote.Contents}");
        } // End method QuoteRandomAsync

        [Command("id")]
        [Summary("Return a quote specified by providing its ID or Alias.")]
        public async Task QuoteSpecificAsync([Remainder] string QuoteIDorAlias)
        { // Quote by ID or alias
            
            Quote? QuoteToQuote = new(); // ? is there so VS will stop yelling at me.

            //Bard monkey, monkey check twice, monkey make QuoteID string so not have to worry if id or alias is entered oo oo aa aa
            if (QuoteListMaster.Exists(EachQuote => EachQuote.ID.ToString() == QuoteIDorAlias)) // Check if QuoteIDorAlias matches any of the IDs
            { QuoteToQuote = QuoteListMaster.Find(EachQuote => EachQuote.ID.ToString() == QuoteIDorAlias); } // If it does, use that quote

            else if(QuoteListMaster.Exists(EachQuote => EachQuote.Alias == QuoteIDorAlias)) // Check if QuoteIDorAlias matches any of the Aliases
            { QuoteToQuote = QuoteListMaster.Find(EachQuote => EachQuote.Alias == QuoteIDorAlias); } // If it does, use that quote

            else // If QuiteIDorAlias didn't match any IDs or Aliases in QuoteListMaster, then assume it doesn't exist and return.
            { await ReplyAsync($"Quote with ID or alias `{QuoteIDorAlias}` does not exist in the library."); return; }

            // If the quote was found, then we can just return it! :D
#pragma warning disable CS8602 // Dereference of a possibly null reference. Quiet!!!!! It's not null! >:(
            await ReplyAsync($"Quote {QuoteToQuote.ID} by {QuoteToQuote.AuthorNickname}:\n{QuoteToQuote.Contents}");
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        } // End method QuoteSpecificAsync

        [Command("add")]
        [RequireContext(ContextType.Guild)] // Disallow quotes being added via DM
        [Summary("Create a request to add a quote. If it's an image, the text should be a discord//attachment link.")]
        public async Task QuoteAddAsync([Remainder] string QuoteContents)
        { // allow any user to request a quote's addition to the library
            
            // Someday, add command/logic to allow adding Alias prop, or integrate Alias with ID or something.
            Quote QuoteToAdd = new()
            {
                AuthorID = Context.Message.Author.Id,
                AuthorNickname = Context.Message.Author.Username,
                Contents = QuoteContents.Replace('@', ' '),
                MessageID = Context.Message.Id,
                GuildID = Context.Guild.Id
            };

            // If a quote exists already with the same contents, then don't add a duplicate.
            if (QuoteListMaster.Any(EachQuote => EachQuote.Contents == QuoteToAdd.Contents) ||
                QuoteListPending.Any(EachQuote => EachQuote.Contents == QuoteToAdd.Contents)) 
            { await ReplyAsync("This quote is already in the library, or is already pending approval."); return; }

            // Set the quote ID here since race conditions are going to make this crigne
            if(QuoteListPending.Count == 0)
            {
                QuoteToAdd.ID = QuoteListMaster[^1].ID + 1;
            }
            else
            {
                QuoteToAdd.ID = QuoteListPending[^1].ID + 1;
            }

            // Add the new quote to the Pending list, and request the quote approval.
            QuoteListPending.Add(QuoteToAdd);
            await ReplyAsync("Your e-mail has been sent! Quote is awaiting moderator approval.");
            await RequestQuoteApprovalAsync(Context, QuoteToAdd);

        } // End method QuoteAddAsync

        [Command("remove")]
        [RequireUserPermission(GuildPermission.ViewAuditLog)] //Admin-only.
        [Summary("Allow removal of quotes from the library. Requires user to have GuildPermission.ViewAuditLog.")]
        public async Task QuoteDeleteAsync([Remainder] int QuoteID)
        {
            // Check to see if the ID exists in the library first.
            if(!QuoteListMaster.Exists(EachQuote => EachQuote.ID == QuoteID))
            { await ReplyAsync($"Quote with ID `{QuoteID}` does not exist in the library."); return; }

            // So now it's certain the quote is in there, so take it out! Display it too so we know what quote was removed.
#pragma warning disable CS8602 // Dereference of a possibly null reference. SHHHHHH!!!!!
            _ = await ReplyAsync($"Removing from the library:\n{QuoteListMaster.Find(EachQuote => EachQuote.ID == QuoteID).Contents}");
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            QuoteListMaster.RemoveAll(EachQuote => EachQuote.ID == QuoteID);
            WriteQuoteListsToFile(); // Write the change to the file.
        } // End method QuoteDeleteAsync

        #endregion Commands

    } // End class Module_Quote
} // End namespace MaiNi
