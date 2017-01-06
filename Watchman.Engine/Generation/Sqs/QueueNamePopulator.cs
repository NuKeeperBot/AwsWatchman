﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Watchman.AwsResources;
using Watchman.AwsResources.Services.Sqs;
using Watchman.Configuration;
using Watchman.Engine.Logging;
using SqsConfig = Watchman.Configuration.Sqs;

namespace Watchman.Engine.Generation.Sqs
{
    public class QueueNamePopulator
    {
        private readonly IAlarmLogger _logger;
        private readonly IResourceSource<QueueData> _queueSource;

        public QueueNamePopulator(
            IAlarmLogger logger,
            IResourceSource<QueueData> queueSource)
        {
            _logger = logger;
            _queueSource = queueSource;
        }

        public async Task PopulateSqsNames(AlertingGroup alertingGroup)
        {
            alertingGroup.Sqs.Queues = await ExpandPatterns(alertingGroup.Sqs, alertingGroup.Name);
        }

        private async Task<List<Queue>> ExpandPatterns(SqsConfig sqs, string alertingGroupName)
        {
            var queuesWithoutPatterns = sqs.Queues
                .Where(t => string.IsNullOrWhiteSpace(t.Pattern))
                .ToList();

            var patterns = sqs.Queues
                .Where(t => !string.IsNullOrWhiteSpace(t.Pattern))
                .ToList();

            var queuesFromPatterns = new List<Queue>();

            foreach (var queuePattern in patterns)
            {
                var matches = await GetPatternMatches(queuePattern, alertingGroupName);

                // filter out duplicates
                matches = matches
                    .Where(match => queuesFromPatterns.All(t => t.Name != match.Name))
                    .ToList();

                matches = matches
                    .Where(match => queuesWithoutPatterns.All(t => t.Name != match.Name))
                    .ToList();

                queuesFromPatterns.AddRange(matches);
            }

            return queuesWithoutPatterns
                .Union(queuesFromPatterns)
                .ToList();
        }
        private async Task<IList<Queue>> GetPatternMatches(Queue queuePattern, string alertingGroupName)
        {
            var queueNames = await _queueSource.GetResourceNamesAsync();

            var matches = queueNames
                .WhereRegexIsMatch(queuePattern.Pattern)
                .Select(tn => PatternToQueue(queuePattern, tn))
                .ToList();

            if (matches.Count == 0)
            {
                _logger.Info($"{alertingGroupName} pattern '{queuePattern.Pattern}' matched no table names");
            }
            else if (matches.Count == queueNames.Count)
            {
                _logger.Info($"{alertingGroupName} pattern '{queuePattern.Pattern}' matched all table names");
            }
            else
            {
                _logger.Detail($"{alertingGroupName}  pattern '{queuePattern.Pattern}' matched {matches.Count} of {queueNames.Count} table names");
            }

            return matches;
        }

        private static Queue PatternToQueue(Queue pattern, string queueName)
        {
            return new Queue
            {
                Name = queueName,
                Pattern = null,
                LengthThreshold = pattern.LengthThreshold,
                OldestMessageThreshold = pattern.OldestMessageThreshold,
                Errors = new ErrorQueue(pattern.Errors)
            };
        }
    }
}