using System;
using Serilog;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SimCorp.IMS.Project.Policies.TFS;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace SimCorp.IMS.Project.Policies
{
    /// <summary>
    /// This policy checks whether user's pending changes have to be reviewed by CODEOWNERS.
    /// </summary>
    public class CodeOwnersPolicy : ValidWorkItemPolicy
    {
        private static readonly ILogger log = Log.ForContext<CodeOwnersPolicy>();
        protected override string ShortName => "CODEOWNERS Policy";
        public override string TypeDescription => "This policy checks whether user's pending changes have to be reviewed by CODEOWNERS";

        private string UserAlias;
        private IEnumerable<string> SubTrees;
        private PendingChange[] CheckedPendingChanges;
        private Dictionary<string, List<string>> FlattenMails; 

        private WorkItemAdapter.List EvaluateWorkItems()
        {
            var workItemCheckinInfos = GetCheckinWorkItems();
            return new WorkItemAdapter.List();
        }

        protected override PolicyFailure[] EvaluateChanges()
        {
            if (!ValidateVariables()) {
                return new PolicyFailure[0];
            }

            var errors = CheckedPendingChanges
                         .Where(WarningNeeded)
                         .Select(FormatWarning)
                         .ToList();

            AddErrors(errors);
            return AllPolicyFailures;
        }

        private string FormatWarning(PendingChange pendingChange)
        {
            var subTree = GetTheMostSpecificSubTree(pendingChange.LocalItem);
            var emails = FlattenMails[subTree];

            return $"Items in the subtree \"{subTree.ToUpper()}\" have CODEOWNERS. "
                 + $"Please include anyone from the list below to a code review: "
                 + $"{ string.Join(" ", emails.ToArray())}";
        }

        /// <summary>
        /// Checks whether user is a CODEOWNER or a pending change subtree is not found in the CODEOWNERS file
        /// </summary>
        bool WarningNeeded(PendingChange pendingChange)
        {
            var subTree = GetTheMostSpecificSubTree(pendingChange.LocalItem);

            return FlattenMails.ContainsKey(subTree)
                   ? !(IsACodeOwner(String.Join(" ", FlattenMails[subTree].ToArray()), UserAlias))
                   : false;
        }

        /// <summary>
        /// Return user's alias in a form of "xxxx" using the default form of "SDCOM\\XXXX"
        /// </summary>
        public string GetUserAlias(string pendingSetOwner)
        {
            return pendingSetOwner
                   .ToLowerInvariant()
                   .Substring(pendingSetOwner.IndexOf('\\') + 1);
        }

        /// <summary>
        /// Checks that the policy has all the needed variables for its work
        /// </summary>
        public bool ValidateVariables() 
        {
            CheckedPendingChanges = PendingCheckin.PendingChanges.CheckedPendingChanges;
            if (!CheckedPendingChanges.Any())
                return false;

            UserAlias = GetUserAlias(CheckedPendingChanges.FirstOrDefault().PendingSetOwner);
            if (string.IsNullOrEmpty(UserAlias))
                return false;

            string codeOwnersPath = GetCodeOwnersPath(CheckedPendingChanges.FirstOrDefault().LocalItem);
            if (string.IsNullOrEmpty(codeOwnersPath))
                return false;

            if (!CheckCodeOwnersExists(codeOwnersPath))
                return false;

            var codeOwnersContent = GetCodeOwnersFile(codeOwnersPath);
            if (!codeOwnersContent.Any())
                return false;

            SubTrees = GetSubTrees(codeOwnersContent);
            if (!SubTrees.Any())
                return false;

            var codeOwnersMails = GetCodeOwnersMails(codeOwnersContent);
            if (!codeOwnersMails.Any())
                return false;

            var groupDefinitions = GetGroupDefinitions(codeOwnersContent);
            var recursiveGroupDefinitions = GetRecursiveGroupDefinitions(groupDefinitions);

            FlattenMails = GetFlattenMails(recursiveGroupDefinitions, codeOwnersMails);
            return true;
        }

        /// <summary>
        /// Find path to the CODEOWNERS file
        /// </summary>
        public string GetCodeOwnersPath(string localItem)
        {
            var imsIndex = localItem.LastIndexOf("IMS\\", StringComparison.OrdinalIgnoreCase);
            
            return (imsIndex == -1) // Pattern is not found
                ? string.Empty
                : localItem.Substring(0, imsIndex + 4) + "CODEOWNERS";
        }

        /// <summary>
        /// Check that CodeOwners file exists
        /// </summary>
        public bool CheckCodeOwnersExists(string codeOwnersPath)
        {
            return File.Exists(codeOwnersPath);
        }

        /// <summary>
        /// Loads the CodeOwners file from a local path
        /// </summary>
        public IEnumerable<string> GetCodeOwnersFile(string codeOwnersPath)
        {
            return File.ReadLines(codeOwnersPath, Encoding.UTF8)
                       .Where (line => line != "")                  // Skip empty lines
                       .Select(line => line.ToLowerInvariant())     // Convert everything to lowercase
                       .Select(line => line.Replace("/", "\\"));    // Convert to win standard with backslashes
        }

        /// <summary>
        /// Find latest line in CODEOWNERS file that matches to the LocalItem
        /// This is needed because order in CODEOWNERS file is important; the last matching pattern takes the most precedence.
        /// When someone modifies APL files, only @foapl-owner and not the global owner(s) will be requested for a review:
        /// FrontOfficeData/              @fo-owner
        /// FrontOfficeData/APL/          @foapl-owner
        /// </summary>
        public string GetTheMostSpecificSubTree(string localItem)
        {
            var matchedSubTree = SubTrees
                                 .Where(x => localItem.ToLower().Contains(x))
                                 .LastOrDefault();

            return string.IsNullOrWhiteSpace(matchedSubTree)
                         ? string.Empty
                         : matchedSubTree;
        }

        /// <summary>
        /// Loads all the followed SubTrees for the CODEOWNERS file. Needed in order to loop through them and find the most specific one
        /// </summary>
        public IEnumerable<string> GetSubTrees(IEnumerable<string> codeOwnersContent)
        {
            return codeOwnersContent
                   .Where((line => !line.StartsWith("#")))                          // Omit comments
                   .Select(line => line.Split(new char[] { ' ', '\t' }).First())    // Select the SubTree name only (first value before \t or \s)
                   .Select(line => line.TrimEnd('/', '\\'));                        // Trim the last slash or backslash
        }

        /// <summary>
        /// Builds group definitions to replace group names with valid emails
        /// </summary>
        public Dictionary<string, string> GetGroupDefinitions(IEnumerable<string> codeOwnersContent)
        {
            return codeOwnersContent
                   .Where(line => line.Contains("=") && line.Contains("@"))
                   .Select(x => x.Split('='))
                   .Where(x => x.Length > 1)
                   .ToDictionary(x => x[0].Trim('#', ' ', '\t'), x => x[1].Trim('#', ' ', '\t'));
        }

        public Dictionary<string, string> GetRecursiveGroupDefinitions(Dictionary<string, string> groupDefinitions)
        {
            // This is not a bulletproof analytically developed solution.
            int loopOnceMore = 0;
            int maxLoopValue = 10;
            do
            {
                foreach (var groupDefinition in groupDefinitions.ToList())
                {
                    string[] lineOfGroupsOrMails = groupDefinition.Value.Split(new[] { ' ', ';', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string groupOrMail in lineOfGroupsOrMails)
                    {
                        if (groupDefinitions.ContainsKey(groupOrMail))
                        {
                            groupDefinitions[groupDefinition.Key] = groupDefinitions[groupDefinition.Key].Replace(groupOrMail, groupDefinitions[groupOrMail]);
                            loopOnceMore += 1;
                        }
                    }
                }

                loopOnceMore -= 1;
                maxLoopValue -= 1;

            } while (loopOnceMore > 0 && maxLoopValue > 0);

            return groupDefinitions;
        }
        
        /// <summary>
        /// Loads a dictionary, with key being a SubTree and value being a list of mail strings
        /// </summary>
        public Dictionary<string, List <string>> GetCodeOwnersMails(IEnumerable<string> codeOwnersContent) 
        {
            var coPattern = @"^([\/\w\-\\\.]+)"; // Every word/dot/backslash characters in the beginning of a line until a whitespace will be a key, everything else - value
            return codeOwnersContent
                   .Where(l => !l.StartsWith("#"))
                   .ToDictionary(l => Regex.Match(l, coPattern).Value.TrimEnd('/', '\\').Trim(),
                                 l => Regex.Replace(l, coPattern, " ").Trim())
                   .ToDictionary(x => x.Key, x => x.Value.Split(new[] { ' ', ';', '\t', ',' }, 
                   StringSplitOptions.RemoveEmptyEntries).ToList());
        }

        /// <summary>
        /// Checks whether user is a codeowner
        /// </summary>
        public bool IsACodeOwner(string flattenMailsLine, string userAlias)
        {
            return flattenMailsLine.Contains(userAlias + "@simcorp.com");
        }

        /// <summary>
        ///  Flatten CODEOWNERS mails from a tree of groups and subgroups
        /// </summary>
        public Dictionary<string, List<string>> GetFlattenMails(Dictionary<string, string> groupDefinitions, Dictionary<string, List <string>> codeOwnersMails)
        {
            return codeOwnersMails
                .Select(x => new { Key = x.Key, Value = ProcessList(x.Value, groupDefinitions) })
                .ToDictionary(x => x.Key, x => x.Value);
        }

        private static List<string> ProcessList(List<string> coMails, Dictionary<string, string> groupDefinitions)
        {
            return coMails
                .Select(x => groupDefinitions.ContainsKey(x) ? groupDefinitions[x] : x)
                .ToList();
        }
    }
}